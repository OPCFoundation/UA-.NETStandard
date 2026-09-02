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
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// One resolved (or unresolved) dependency edge between two documents.
    /// </summary>
    public sealed class WotDependency
    {
        /// <summary>
        /// Initializes a new dependency edge.
        /// </summary>
        public WotDependency(
            string sourceXid,
            string targetHref,
            string? targetXid,
            string refType,
            bool resolved)
        {
            SourceXid = sourceXid;
            TargetHref = targetHref;
            TargetXid = targetXid;
            RefType = refType;
            Resolved = resolved;
        }

        /// <summary>
        /// Gets the xid of the dependent document.
        /// </summary>
        public string SourceXid { get; }

        /// <summary>
        /// Gets the raw href/URI of the dependency.
        /// </summary>
        public string TargetHref { get; }

        /// <summary>
        /// Gets the xid of the resolved target document, if any.
        /// </summary>
        public string? TargetXid { get; }

        /// <summary>
        /// Gets the dependency kind (tm:extends / tm:ref / links.rel=type).
        /// </summary>
        public string RefType { get; }

        /// <summary>
        /// Gets whether the dependency resolved to a stored document.
        /// </summary>
        public bool Resolved { get; }
    }

    /// <summary>
    /// A dependency closure: a set of resources that must be materialized
    /// together, with Thing Models topologically ordered before the Thing
    /// Descriptions that depend on them. A closure is the default unit of
    /// atomicity for a refresh.
    /// </summary>
    public sealed class WotDependencyClosure
    {
        internal WotDependencyClosure(
            string key,
            ImmutableArray<WotResource> members,
            ImmutableArray<WotResource> orderedResources,
            ImmutableArray<WotDependency> dependencies,
            ImmutableArray<string> diagnostics,
            bool hasCycle,
            bool hasMissingDependency)
        {
            Key = key;
            Members = members;
            OrderedResources = orderedResources;
            Dependencies = dependencies;
            Diagnostics = diagnostics;
            HasCycle = hasCycle;
            HasMissingDependency = hasMissingDependency;
        }

        /// <summary>
        /// Gets the stable closure key (sorted member xids).
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets every member of the closure (populated even on a cycle).
        /// </summary>
        public ImmutableArray<WotResource> Members { get; }

        /// <summary>
        /// Gets the resources in topological (dependency-first) order.
        /// </summary>
        public ImmutableArray<WotResource> OrderedResources { get; }

        /// <summary>
        /// Gets the dependency edges within the closure.
        /// </summary>
        public ImmutableArray<WotDependency> Dependencies { get; }

        /// <summary>
        /// Gets the diagnostics for the closure.
        /// </summary>
        public ImmutableArray<string> Diagnostics { get; }

        /// <summary>
        /// Gets whether the closure contains a dependency cycle.
        /// </summary>
        public bool HasCycle { get; }

        /// <summary>
        /// Gets whether the closure has an unresolved dependency.
        /// </summary>
        public bool HasMissingDependency { get; }

        /// <summary>
        /// Gets whether the closure is projectable (no cycle, no missing dependency).
        /// </summary>
        public bool IsProjectable => !HasCycle && !HasMissingDependency;
    }

    /// <summary>
    /// Builds the TD/TM dependency graph from a registry snapshot and partitions
    /// it into deterministic dependency closures. References are extracted from
    /// <c>links</c> (rel = tm:extends / type / tm:submodel), a top-level
    /// <c>tm:extends</c>, and <c>tm:ref</c> pointers, then resolved against the
    /// registry by Thing id, xid, or resource id.
    /// </summary>
    public static class WotDependencyGraph
    {
        /// <summary>
        /// The dependency kind of an event affordance's EventType fast path.
        /// </summary>
        public const string EventTypeRefType = "uav:eventType";

        /// <summary>
        /// The dependency kind of the EventType an explicit select clause
        /// references.
        /// </summary>
        public const string EventSelectClauseRefType = "uav:eventSelectClauses";

        /// <summary>
        /// Resolves a WoT reference href to a stored resource, or <c>null</c>.
        /// </summary>
        public static WotResource? Resolve(WotRegistrySnapshot snapshot, string href)
        {
            if (snapshot is null || string.IsNullOrWhiteSpace(href))
            {
                return null;
            }
            string trimmed = TrimFragment(href);
            // Prefer Thing Models, then any resource, matching by thing id, xid or resource id.
            return MatchIn(snapshot.ResourcesOfKind(WoTDocumentKindEnum.ThingModel), trimmed)
                ?? MatchIn(snapshot.AllResources(), trimmed);
        }

        /// <summary>
        /// Extracts the outgoing dependency references of a single document.
        /// </summary>
        public static IReadOnlyList<(string Href, string RefType)> ExtractReferences(
            ReadOnlyMemory<byte> document,
            int maxJsonDepth)
        {
            var references = new List<(string, string)>();
            try
            {
                var options = new JsonDocumentOptions { MaxDepth = maxJsonDepth };
                using var json = JsonDocument.Parse(document, options);
                JsonElement root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return references;
                }
                CollectLinks(root, references);
                CollectExtends(root, references);
                CollectProjects(root, references);
                CollectEventTypeRefs(root, references);
                CollectTmRefs(root, references, 0, maxJsonDepth);
            }
            catch (JsonException)
            {
                // A document that cannot be parsed contributes no edges; its own
                // projection reports the parse failure.
            }
            return references;
        }

        /// <summary>
        /// Builds the dependency closures for the selected resources. Selected
        /// resources are grouped into weakly-connected components (so a shared
        /// Thing Model lands in a single closure), then each component is
        /// topologically ordered.
        /// </summary>
        public static async ValueTask<ImmutableArray<WotDependencyClosure>> BuildClosuresAsync(
            WotRegistrySnapshot snapshot,
            IReadOnlyCollection<WotResource> selected,
            int maxJsonDepth,
            Func<WotResourceVersion, CancellationToken, ValueTask<ByteString>> readContent,
            CancellationToken cancellationToken)
        {
            if (selected.Count == 0)
            {
                return [];
            }

            // Expand the selection to include resolvable transitive dependencies.
            var byXid = new Dictionary<string, WotResource>(StringComparer.Ordinal);
            var queue = new Queue<WotResource>();
            foreach (WotResource resource in selected)
            {
                if (!byXid.ContainsKey(resource.Xid))
                {
                    byXid[resource.Xid] = resource;
                    queue.Enqueue(resource);
                }
            }

            var edges = new Dictionary<string, List<WotDependency>>(StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WotResource resource = queue.Dequeue();
                var list = new List<WotDependency>();
                edges[resource.Xid] = list;
                WotResourceVersion? version = resource.DefaultVersion;
                if (version is null)
                {
                    continue;
                }
                ByteString content = await readContent(version, cancellationToken)
                    .ConfigureAwait(false);
                foreach ((string href, string refType) in ExtractReferences(
                    content.Span.ToArray(), maxJsonDepth))
                {
                    WotResource? target = Resolve(snapshot, href);
                    list.Add(new WotDependency(
                        resource.Xid, href, target?.Xid, refType, target is not null));
                    if (target is not null && !byXid.ContainsKey(target.Xid))
                    {
                        byXid[target.Xid] = target;
                        queue.Enqueue(target);
                    }
                }
            }

            // Weakly-connected components via union-find over resolved edges.
            var parent = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string xid in byXid.Keys)
            {
                parent[xid] = xid;
            }
            foreach (List<WotDependency> list in edges.Values)
            {
                foreach (WotDependency edge in list)
                {
                    if (edge.Resolved &&
                        edge.TargetXid is not null &&
                        byXid.ContainsKey(edge.TargetXid))
                    {
                        Union(parent, edge.SourceXid, edge.TargetXid);
                    }
                }
            }

            var components = new Dictionary<string, List<WotResource>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, WotResource> entry in byXid)
            {
                string root = Find(parent, entry.Key);
                if (!components.TryGetValue(root, out List<WotResource>? members))
                {
                    members = [];
                    components[root] = members;
                }
                members.Add(entry.Value);
            }

            ImmutableArray<WotDependencyClosure>.Builder closures =
                ImmutableArray.CreateBuilder<WotDependencyClosure>();
            foreach (List<WotResource> members in components.Values)
            {
                closures.Add(BuildClosure(members, edges, byXid));
            }
            // Deterministic order by closure key.
            return [.. closures.OrderBy(c => c.Key, StringComparer.Ordinal)];
        }

        private static WotDependencyClosure BuildClosure(
            List<WotResource> members,
            Dictionary<string, List<WotDependency>> edges,
            Dictionary<string, WotResource> byXid)
        {
            var memberXids = new HashSet<string>(members.Select(m => m.Xid), StringComparer.Ordinal);
            ImmutableArray<WotDependency>.Builder dependencies = ImmutableArray.CreateBuilder<WotDependency>();
            ImmutableArray<string>.Builder diagnostics = ImmutableArray.CreateBuilder<string>();
            bool missing = false;

            // Adjacency (source depends on target): target must be ordered first.
            var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (WotResource member in members)
            {
                adjacency[member.Xid] = [];
            }
            foreach (WotResource member in members)
            {
                if (!edges.TryGetValue(member.Xid, out List<WotDependency>? list))
                {
                    continue;
                }
                foreach (WotDependency edge in list)
                {
                    dependencies.Add(edge);
                    if (!edge.Resolved)
                    {
                        missing = true;
                        diagnostics.Add(
                            $"Unresolved {edge.RefType} dependency '{edge.TargetHref}' " +
                            $"referenced by '{edge.SourceXid}'.");
                    }
                    else if (edge.TargetXid is not null && memberXids.Contains(edge.TargetXid))
                    {
                        adjacency[member.Xid].Add(edge.TargetXid);
                    }
                }
            }

            (ImmutableArray<WotResource> ordered, bool hasCycle) = TopologicalSort(
                members, adjacency, byXid);
            if (hasCycle)
            {
                diagnostics.Add(
                    "Dependency cycle detected among: " +
                    string.Join(", ", members.Select(m => m.Xid).OrderBy(x => x, StringComparer.Ordinal)));
            }

            string key = string.Join(
                "|", members.Select(m => m.Xid).OrderBy(x => x, StringComparer.Ordinal));
            var memberArray = members
                .OrderBy(m => m.Xid, StringComparer.Ordinal)
                .ToImmutableArray();
            return new WotDependencyClosure(
                key,
                memberArray,
                ordered,
                dependencies.ToImmutable(),
                diagnostics.ToImmutable(),
                hasCycle,
                missing);
        }

        private static (ImmutableArray<WotResource> Ordered, bool HasCycle) TopologicalSort(
            List<WotResource> members,
            Dictionary<string, List<string>> adjacency,
            Dictionary<string, WotResource> byXid)
        {
            // 0 = unvisited, 1 = in-progress, 2 = done.
            var color = new Dictionary<string, int>(StringComparer.Ordinal);
            var ordered = new List<WotResource>();
            bool hasCycle = false;

            // Deterministic iteration order.
            IEnumerable<string> roots = members
                .Select(m => m.Xid)
                .OrderBy(x => x, StringComparer.Ordinal);

            void Visit(string xid)
            {
                if (hasCycle)
                {
                    return;
                }
                color.TryGetValue(xid, out int state);
                if (state == 2)
                {
                    return;
                }
                if (state == 1)
                {
                    hasCycle = true;
                    return;
                }
                color[xid] = 1;
                foreach (string dependency in adjacency[xid]
                    .OrderBy(x => x, StringComparer.Ordinal))
                {
                    Visit(dependency);
                    if (hasCycle)
                    {
                        return;
                    }
                }
                color[xid] = 2;
                ordered.Add(byXid[xid]);
            }

            foreach (string root in roots)
            {
                Visit(root);
            }

            return hasCycle
                ? (ImmutableArray<WotResource>.Empty, true)
                : ([.. ordered], false);
        }

        private static WotResource? MatchIn(IEnumerable<WotResource> resources, string href)
        {
            foreach (WotResource resource in resources)
            {
                if (string.Equals(resource.ThingId, href, StringComparison.Ordinal) ||
                    string.Equals(resource.Xid, href, StringComparison.Ordinal) ||
                    string.Equals(RegistryUri(resource), href, StringComparison.Ordinal) ||
                    string.Equals(resource.ResourceId, href, StringComparison.Ordinal) ||
                    href.EndsWith("/" + resource.ResourceId, StringComparison.Ordinal))
                {
                    return resource;
                }
            }
            return null;
        }

        private static string RegistryUri(WotResource resource)
        {
            return $"urn:wot:{resource.GroupId}/{resource.ResourceId}";
        }

        private static string TrimFragment(string href)
        {
            int hash = href.AsSpan().IndexOf('#');
            return hash >= 0 ? href[..hash] : href;
        }

        private static void CollectLinks(
            JsonElement root, List<(string, string)> references)
        {
            if (!root.TryGetProperty("links", out JsonElement links) ||
                links.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            foreach (JsonElement link in links.EnumerateArray())
            {
                if (link.ValueKind != JsonValueKind.Object ||
                    !link.TryGetProperty("href", out JsonElement hrefElement) ||
                    hrefElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                string rel = link.TryGetProperty("rel", out JsonElement relElement) &&
                    relElement.ValueKind == JsonValueKind.String
                    ? relElement.GetString() ?? string.Empty
                    : string.Empty;
                if (rel is "tm:extends" or "type" or "tm:submodel" or "collection" or "item" or
                    "ua:Organizes")
                {
                    references.Add((hrefElement.GetString() ?? string.Empty, rel));
                }
            }
        }

        private static void CollectExtends(
            JsonElement root, List<(string, string)> references)
        {
            if (!root.TryGetProperty("tm:extends", out JsonElement extends))
            {
                return;
            }
            switch (extends.ValueKind)
            {
                case JsonValueKind.String:
                    references.Add((extends.GetString() ?? string.Empty, "tm:extends"));
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in extends.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            references.Add((item.GetString() ?? string.Empty, "tm:extends"));
                        }
                        else if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty("href", out JsonElement href) &&
                            href.ValueKind == JsonValueKind.String)
                        {
                            references.Add((href.GetString() ?? string.Empty, "tm:extends"));
                        }
                    }
                    break;
            }
        }

        private static void CollectProjects(
            JsonElement root, List<(string, string)> references)
        {
            // A projection document depends on every document its uav:projects
            // manifest names, so the closure includes the sources and a cyclic
            // projection graph is rejected during dependency resolution.
            if (!root.TryGetProperty("uav:projects", out JsonElement projects) ||
                projects.ValueKind != JsonValueKind.Array)
            {
                return;
            }
            foreach (JsonElement entry in projects.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("href", out JsonElement href) &&
                    href.ValueKind == JsonValueKind.String)
                {
                    references.Add((href.GetString() ?? string.Empty, "uav:projects"));
                }
            }
        }

        /// <summary>
        /// Collects the EventType definitions an event affordance links to,
        /// both the affordance-level fast path and the reference every explicit
        /// select clause carries (WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// A consumer resolves those links against the documents it holds and
        /// never over the network, so the EventType Thing Model has to be a
        /// member of the same closure and has to be loaded before the document
        /// that selects from it. Naming the edges here rather than leaving them
        /// to the generic <c>tm:ref</c> walk is what makes that dependency
        /// visible in a closure's dependency list.
        /// </remarks>
        private static void CollectEventTypeRefs(
            JsonElement root, List<(string, string)> references)
        {
            if (!root.TryGetProperty("events", out JsonElement events) ||
                events.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            foreach (JsonProperty affordance in events.EnumerateObject())
            {
                if (affordance.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                if (affordance.Value.TryGetProperty("tm:ref", out JsonElement reference) &&
                    reference.ValueKind == JsonValueKind.String)
                {
                    references.Add((reference.GetString() ?? string.Empty, EventTypeRefType));
                }
                if (!affordance.Value.TryGetProperty(
                        "uav:eventSelectClauses", out JsonElement clauses) ||
                    clauses.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (JsonElement clause in clauses.EnumerateArray())
                {
                    if (clause.ValueKind == JsonValueKind.Object &&
                        clause.TryGetProperty("tm:ref", out JsonElement clauseRef) &&
                        clauseRef.ValueKind == JsonValueKind.String)
                    {
                        references.Add((
                            clauseRef.GetString() ?? string.Empty, EventSelectClauseRefType));
                    }
                }
            }
        }

        private static void CollectTmRefs(
            JsonElement element,
            List<(string, string)> references,
            int depth,
            int maxDepth)
        {
            if (depth > maxDepth)
            {
                return;
            }
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (depth == 0 &&
                            string.Equals(property.Name, "events", StringComparison.Ordinal))
                        {
                            // CollectEventTypeRefs already named these edges for
                            // what they are; walking them again would state the
                            // same dependency twice under a weaker label.
                            continue;
                        }
                        if (string.Equals(property.Name, "tm:ref", StringComparison.Ordinal) &&
                            property.Value.ValueKind == JsonValueKind.String)
                        {
                            references.Add((property.Value.GetString() ?? string.Empty, "tm:ref"));
                        }
                        else
                        {
                            CollectTmRefs(property.Value, references, depth + 1, maxDepth);
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        CollectTmRefs(item, references, depth + 1, maxDepth);
                    }
                    break;
            }
        }

        private static string Find(Dictionary<string, string> parent, string node)
        {
            string root = node;
            while (!string.Equals(parent[root], root, StringComparison.Ordinal))
            {
                root = parent[root];
            }
            // Path compression.
            while (!string.Equals(parent[node], root, StringComparison.Ordinal))
            {
                string next = parent[node];
                parent[node] = root;
                node = next;
            }
            return root;
        }

        private static void Union(Dictionary<string, string> parent, string a, string b)
        {
            string rootA = Find(parent, a);
            string rootB = Find(parent, b);
            if (!string.Equals(rootA, rootB, StringComparison.Ordinal))
            {
                parent[rootB] = rootA;
            }
        }
    }
}
