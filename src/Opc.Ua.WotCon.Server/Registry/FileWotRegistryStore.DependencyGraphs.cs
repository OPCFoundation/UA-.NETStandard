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
using System.IO;
using System.Linq;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// A durable, file-backed registry store. A storage-root lock serializes loads
    /// and commits across store instances and processes. Every load validates the
    /// primary manifest and all content-addressed blobs; any failure other than an
    /// absent primary manifest with no store-owned recovery artifacts fails closed.
    /// </summary>
    public sealed partial class FileWotRegistryStore
    {
        internal sealed class DependencyGraphDto
        {
            /// <summary>
            /// Gets or sets the edges for <c>DependencyGraphDto</c>.
            /// </summary>
            public DependencyEdgeDto[]? Edges { get; set; }
            /// <summary>
            /// Gets or sets the targets for <c>DependencyGraphDto</c>.
            /// </summary>
            public DependencyTargetDto[]? Targets { get; set; }
        }

        private sealed class DependencyGraphWriter
        {
            /// <param name="snapshot">The WoT dependency snapshot.</param>
            public int GetIndex(WotDependencySnapshot snapshot)
            {
                for (int index = 0; index < m_sources.Count; index++)
                {
                    if (SameGraph(m_sources[index], snapshot))
                    {
                        return index;
                    }
                }
                m_sources.Add(snapshot);
                m_graphs.Add(ToGraphDto(snapshot));
                return m_sources.Count - 1;
            }

            public DependencyGraphDto[]? ToArray()
            {
                return m_graphs.Count == 0 ? null : [.. m_graphs];
            }

            private static bool SameGraph(WotDependencySnapshot left, WotDependencySnapshot right)
            {
                if (ReferenceEquals(left.Graph, right.Graph))
                {
                    return true;
                }
                ArrayOf<WotDependency> leftEdges = left.Graph.Edges;
                ArrayOf<WotDependency> rightEdges = right.Graph.Edges;
                ArrayOf<WotDependencyTargetPin> leftTargets = left.Graph.Targets;
                ArrayOf<WotDependencyTargetPin> rightTargets = right.Graph.Targets;
                if (leftEdges.Count != rightEdges.Count || leftTargets.Count != rightTargets.Count)
                {
                    return false;
                }
                for (int index = 0; index < leftEdges.Count; index++)
                {
                    WotDependency a = leftEdges[index];
                    WotDependency b = rightEdges[index];
                    if (!ReferenceEquals(a, b) &&
                        (a.SourceXid != b.SourceXid || a.TargetHref != b.TargetHref ||
                         a.TargetXid != b.TargetXid || a.RefType != b.RefType || a.Resolved != b.Resolved))
                    {
                        return false;
                    }
                }
                for (int index = 0; index < leftTargets.Count; index++)
                {
                    WotDependencyTargetPin a = leftTargets[index];
                    WotDependencyTargetPin b = rightTargets[index];
                    if (!ReferenceEquals(a, b) &&
                        (a.EdgeIndex != b.EdgeIndex || a.VersionXid != b.VersionXid ||
                         a.DocumentUri != b.DocumentUri || a.VersionNodeId != b.VersionNodeId ||
                         !a.ContentDigest.Span.SequenceEqual(b.ContentDigest.Span) ||
                         !SameOrigin(a.OriginRegistry, b.OriginRegistry)))
                    {
                        return false;
                    }
                }
                return true;
            }

            private static bool SameOrigin(WotRegistryOrigin? left, WotRegistryOrigin? right)
            {
                return ReferenceEquals(left, right) || left is not null && right is not null &&
                    left.OriginUri == right.OriginUri && left.ServerUri == right.ServerUri &&
                    left.RegistryNodeId == right.RegistryNodeId;
            }

            private readonly List<WotDependencySnapshot> m_sources = [];
            private readonly List<DependencyGraphDto> m_graphs = [];
        }

        private sealed class DependencyGraphReader(DependencyGraphDto[]? graphs)
        {
            /// <param name="observation">The dependency snapshot dto.</param>
            public WotDependencySnapshot.ValidatedGraph Read(
                DependencySnapshotDto observation)
            {
                if (observation.GraphIndex is not { } index)
                {
                    return Parse(observation.Edges, observation.Targets);
                }
                if (observation.Edges is not null || observation.Targets is not null ||
                    graphs is null || index < 0 || index >= graphs.Length)
                {
                    throw new InvalidDataException("A dependency observation has an invalid shared graph reference.");
                }
                if (!m_cache.TryGetValue(index, out var graph))
                {
                    DependencyGraphDto source = graphs[index] ??
                        throw new InvalidDataException("A shared dependency graph is null.");
                    graph = Parse(source.Edges, source.Targets);
                    m_cache.Add(index, graph);
                }
                return graph;
            }

            private static WotDependencySnapshot.ValidatedGraph Parse(
                DependencyEdgeDto[]? edges, DependencyTargetDto[]? targets)
            {
                if (edges is null || targets is null)
                {
                    throw new InvalidDataException("A dependency graph is incomplete.");
                }
                if (edges.Any(edge => edge is null) || targets.Any(target => target is null))
                {
                    throw new InvalidDataException("A dependency graph contains a null edge or target.");
                }
                return new WotDependencySnapshot.ValidatedGraph(edges.Select(edge => new WotDependency(
                    edge.SourceXid ?? throw new InvalidDataException("Missing dependency source."),
                    edge.TargetHref ?? throw new InvalidDataException("Missing dependency target."),
                    edge.TargetXid,
                    edge.RefType ?? throw new InvalidDataException("Missing dependency relation."),
                    edge.Resolved)).ToArrayOf(), targets.Select(FromDto).ToArrayOf());
            }

            private readonly Dictionary<int, WotDependencySnapshot.ValidatedGraph> m_cache = [];
        }
    }
}
