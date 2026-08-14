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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// One outgoing reference a stored document makes to another document.
    /// </summary>
    /// <remarks>
    /// A domain extracts these from its own document format - a WoT Thing Model
    /// states them as <c>tm:extends</c> and <c>links</c>, an AAS environment as
    /// required document identities - and the graph resolves and groups them
    /// without knowing either format.
    /// </remarks>
    public sealed class XRegistryDependencyReference
    {
        /// <summary>
        /// Initializes a dependency reference.
        /// </summary>
        /// <param name="href">The raw reference as the document writes it.</param>
        /// <param name="refType">The domain reference kind, for diagnostics.</param>
        /// <exception cref="ArgumentNullException"><paramref name="href"/> is <c>null</c>.</exception>
        public XRegistryDependencyReference(string href, string refType)
        {
            Href = href ?? throw new ArgumentNullException(nameof(href));
            RefType = refType ?? string.Empty;
        }

        /// <summary>
        /// Gets the raw reference as the document writes it.
        /// </summary>
        public string Href { get; }

        /// <summary>
        /// Gets the domain reference kind, used only in diagnostics.
        /// </summary>
        public string RefType { get; }
    }

    /// <summary>
    /// One resolved or unresolved dependency edge between two documents.
    /// </summary>
    public sealed class XRegistryDependency
    {
        /// <summary>
        /// Initializes a dependency edge.
        /// </summary>
        /// <param name="sourceXid">The xid of the dependent document.</param>
        /// <param name="targetHref">The raw reference.</param>
        /// <param name="targetXid">The resolved target xid, if any.</param>
        /// <param name="refType">The domain reference kind.</param>
        public XRegistryDependency(
            string sourceXid,
            string targetHref,
            string? targetXid,
            string refType)
        {
            SourceXid = sourceXid ?? string.Empty;
            TargetHref = targetHref ?? string.Empty;
            TargetXid = targetXid;
            RefType = refType ?? string.Empty;
        }

        /// <summary>
        /// Gets the xid of the dependent document.
        /// </summary>
        public string SourceXid { get; }

        /// <summary>
        /// Gets the raw reference.
        /// </summary>
        public string TargetHref { get; }

        /// <summary>
        /// Gets the xid of the resolved target, or <c>null</c>.
        /// </summary>
        public string? TargetXid { get; }

        /// <summary>
        /// Gets the domain reference kind.
        /// </summary>
        public string RefType { get; }

        /// <summary>
        /// Gets whether the reference resolved to a stored document.
        /// </summary>
        public bool Resolved => TargetXid is not null;
    }

    /// <summary>
    /// A set of resources that must be materialized together, dependency-first.
    /// </summary>
    /// <remarks>
    /// A closure is the unit of atomicity for a refresh: it commits or fails as a
    /// whole, because a document that projects without the type it extends would
    /// publish a node graph the client cannot interpret.
    /// </remarks>
    public sealed class XRegistryDependencyClosure
    {
        /// <summary>
        /// Initializes a dependency closure.
        /// </summary>
        /// <param name="key">The stable closure key.</param>
        /// <param name="members">Every member, populated even on a cycle.</param>
        /// <param name="orderedMembers">The members in dependency-first order.</param>
        /// <param name="dependencies">The edges within the closure.</param>
        /// <param name="diagnostics">The closure diagnostics.</param>
        /// <param name="hasCycle">Whether the closure contains a cycle.</param>
        /// <param name="hasMissingDependency">Whether a reference is unresolved.</param>
        public XRegistryDependencyClosure(
            string key,
            ArrayOf<XRegistryRefreshMember> members,
            ArrayOf<XRegistryRefreshMember> orderedMembers,
            ArrayOf<XRegistryDependency> dependencies,
            ArrayOf<string> diagnostics,
            bool hasCycle,
            bool hasMissingDependency)
        {
            Key = key ?? string.Empty;
            Members = members;
            OrderedMembers = orderedMembers;
            Dependencies = dependencies;
            Diagnostics = diagnostics;
            HasCycle = hasCycle;
            HasMissingDependency = hasMissingDependency;
        }

        /// <summary>
        /// Gets the stable closure key, derived from the sorted member xids.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets every member, populated even when the closure has a cycle.
        /// </summary>
        public ArrayOf<XRegistryRefreshMember> Members { get; }

        /// <summary>
        /// Gets the members in dependency-first order.
        /// </summary>
        /// <remarks>
        /// Empty when the closure has a cycle, because no such order exists.
        /// </remarks>
        public ArrayOf<XRegistryRefreshMember> OrderedMembers { get; }

        /// <summary>
        /// Gets the dependency edges within the closure.
        /// </summary>
        public ArrayOf<XRegistryDependency> Dependencies { get; }

        /// <summary>
        /// Gets the closure diagnostics.
        /// </summary>
        public ArrayOf<string> Diagnostics { get; }

        /// <summary>
        /// Gets whether the closure contains a dependency cycle.
        /// </summary>
        public bool HasCycle { get; }

        /// <summary>
        /// Gets whether the closure has an unresolved dependency.
        /// </summary>
        public bool HasMissingDependency { get; }

        /// <summary>
        /// Gets whether the closure can be projected.
        /// </summary>
        public bool IsProjectable => !HasCycle && !HasMissingDependency;
    }

    /// <summary>
    /// Builds the document dependency graph for a refresh and partitions it into
    /// deterministic closures.
    /// </summary>
    /// <remarks>
    /// The selection is expanded across resolvable references so a shared
    /// dependency pulls its dependents into the same closure, the closures are
    /// the weakly-connected components of the resulting graph, and each component
    /// is topologically ordered so a document is preceded by everything it
    /// depends on.
    /// </remarks>
    public static class XRegistryDependencyGraph
    {
        /// <summary>
        /// Builds the dependency closures for the selected members.
        /// </summary>
        /// <param name="selected">The members the refresh starts from.</param>
        /// <param name="extractReferencesAsync">
        /// Extracts the outgoing references of one member from its stored document.
        /// </param>
        /// <param name="resolve">
        /// Resolves a raw reference to a stored member, or returns <c>null</c> when
        /// the registry does not hold it.
        /// </param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The closures, ordered by key.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="extractReferencesAsync"/> or <paramref name="resolve"/> is <c>null</c>.
        /// </exception>
        public static async ValueTask<ArrayOf<XRegistryDependencyClosure>> BuildClosuresAsync(
            ArrayOf<XRegistryRefreshMember> selected,
            Func<XRegistryRefreshMember, CancellationToken,
                ValueTask<ArrayOf<XRegistryDependencyReference>>> extractReferencesAsync,
            Func<string, XRegistryRefreshMember?> resolve,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(extractReferencesAsync);
            ArgumentNullException.ThrowIfNull(resolve);

            if (selected.Count == 0)
            {
                return [];
            }

            // Expand the selection across resolvable transitive dependencies, so a
            // closure is complete even when the caller selected only one of its
            // members.
            var byXid = new Dictionary<string, XRegistryRefreshMember>(StringComparer.Ordinal);
            var queue = new Queue<XRegistryRefreshMember>();
            foreach (XRegistryRefreshMember member in selected)
            {
                if (byXid.TryAdd(member.Xid, member))
                {
                    queue.Enqueue(member);
                }
            }

            var edges = new Dictionary<string, List<XRegistryDependency>>(StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                XRegistryRefreshMember member = queue.Dequeue();
                var list = new List<XRegistryDependency>();
                edges[member.Xid] = list;

                ArrayOf<XRegistryDependencyReference> references =
                    await extractReferencesAsync(member, ct).ConfigureAwait(false);
                foreach (XRegistryDependencyReference reference in references)
                {
                    XRegistryRefreshMember? target = resolve(reference.Href);
                    list.Add(new XRegistryDependency(
                        member.Xid, reference.Href, target?.Xid, reference.RefType));
                    if (target is not null && byXid.TryAdd(target.Xid, target))
                    {
                        queue.Enqueue(target);
                    }
                }
            }

            // Weakly-connected components via union-find over the resolved edges.
            var parent = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string xid in byXid.Keys)
            {
                parent[xid] = xid;
            }
            foreach (List<XRegistryDependency> list in edges.Values)
            {
                foreach (XRegistryDependency edge in list)
                {
                    if (edge.TargetXid is not null && byXid.ContainsKey(edge.TargetXid))
                    {
                        Union(parent, edge.SourceXid, edge.TargetXid);
                    }
                }
            }

            var components = new Dictionary<string, List<XRegistryRefreshMember>>(
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, XRegistryRefreshMember> entry in byXid)
            {
                string root = Find(parent, entry.Key);
                if (!components.TryGetValue(root, out List<XRegistryRefreshMember>? members))
                {
                    members = [];
                    components[root] = members;
                }
                members.Add(entry.Value);
            }

            var closures = new List<XRegistryDependencyClosure>(components.Count);
            foreach (List<XRegistryRefreshMember> members in components.Values)
            {
                closures.Add(BuildClosure(members, edges));
            }

            return closures.OrderBy(c => c.Key, StringComparer.Ordinal).ToArrayOf();
        }

        private static XRegistryDependencyClosure BuildClosure(
            List<XRegistryRefreshMember> members,
            Dictionary<string, List<XRegistryDependency>> edges)
        {
            var memberXids = new HashSet<string>(
                members.Select(m => m.Xid), StringComparer.Ordinal);
            var dependencies = new List<XRegistryDependency>();
            var diagnostics = new List<string>();
            bool missing = false;

            // Adjacency reads "source depends on target", so a target is ordered
            // before the member that lists it.
            var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (XRegistryRefreshMember member in members)
            {
                adjacency[member.Xid] = [];
            }
            foreach (XRegistryRefreshMember member in members)
            {
                if (!edges.TryGetValue(member.Xid, out List<XRegistryDependency>? list))
                {
                    continue;
                }
                foreach (XRegistryDependency edge in list)
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

            (ArrayOf<XRegistryRefreshMember> ordered, bool hasCycle) = TopologicalSort(
                members, adjacency);
            if (hasCycle)
            {
                diagnostics.Add(
                    "Dependency cycle detected among: " +
                    string.Join(", ", members
                        .Select(m => m.Xid)
                        .OrderBy(x => x, StringComparer.Ordinal)));
            }

            string key = string.Join(
                "|", members.Select(m => m.Xid).OrderBy(x => x, StringComparer.Ordinal));

            return new XRegistryDependencyClosure(
                key,
                members.OrderBy(m => m.Xid, StringComparer.Ordinal).ToArrayOf(),
                ordered,
                dependencies.ToArrayOf(),
                diagnostics.ToArrayOf(),
                hasCycle,
                missing);
        }

        private static (ArrayOf<XRegistryRefreshMember> Ordered, bool HasCycle) TopologicalSort(
            List<XRegistryRefreshMember> members,
            Dictionary<string, List<string>> adjacency)
        {
            var byXid = new Dictionary<string, XRegistryRefreshMember>(StringComparer.Ordinal);
            foreach (XRegistryRefreshMember member in members)
            {
                byXid[member.Xid] = member;
            }

            // 0 = unvisited, 1 = in progress, 2 = done.
            var color = new Dictionary<string, int>(StringComparer.Ordinal);
            var ordered = new List<XRegistryRefreshMember>();
            bool hasCycle = false;

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
                if (adjacency.TryGetValue(xid, out List<string>? targets))
                {
                    foreach (string target in targets.OrderBy(x => x, StringComparer.Ordinal))
                    {
                        Visit(target);
                        if (hasCycle)
                        {
                            return;
                        }
                    }
                }
                color[xid] = 2;
                if (byXid.TryGetValue(xid, out XRegistryRefreshMember? member))
                {
                    ordered.Add(member);
                }
            }

            foreach (string xid in members.Select(m => m.Xid).OrderBy(x => x, StringComparer.Ordinal))
            {
                Visit(xid);
                if (hasCycle)
                {
                    return ([], true);
                }
            }

            return (ordered.ToArrayOf(), false);
        }

        private static string Find(Dictionary<string, string> parent, string node)
        {
            string root = node;
            while (!string.Equals(parent[root], root, StringComparison.Ordinal))
            {
                root = parent[root];
            }
            // Path compression keeps repeated lookups linear for wide closures.
            string current = node;
            while (!string.Equals(parent[current], root, StringComparison.Ordinal))
            {
                string next = parent[current];
                parent[current] = root;
                current = next;
            }
            return root;
        }

        private static void Union(Dictionary<string, string> parent, string left, string right)
        {
            string leftRoot = Find(parent, left);
            string rightRoot = Find(parent, right);
            if (string.Equals(leftRoot, rightRoot, StringComparison.Ordinal))
            {
                return;
            }
            // Attach deterministically so the component root does not depend on
            // the order the edges happened to arrive in.
            if (string.CompareOrdinal(leftRoot, rightRoot) < 0)
            {
                parent[rightRoot] = leftRoot;
            }
            else
            {
                parent[leftRoot] = rightRoot;
            }
        }
    }
}
