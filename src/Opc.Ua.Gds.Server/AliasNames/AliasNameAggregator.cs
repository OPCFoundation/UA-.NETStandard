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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Gds.Server.AliasNames
{
    /// <summary>
    /// The master AliasNames list of a GDS that implements the AliasName
    /// Server facet (OPC 10000-17 Annex C): merges the snapshots pulled from
    /// the registered AliasName Servers, keeps their ServerUris in the
    /// server's <c>ServerArray</c> and answers <c>FindAlias</c> /
    /// <c>FindAliasVerbose</c> over the merged list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Merging follows Annex C.1 and C.2: categories with the same Name part
    /// of the BrowseName at the same place of the hierarchy become one
    /// category, and aliases with the same name in the same category become
    /// one alias whose targets are the union of the sources' targets. A
    /// target on another Server is an <see cref="ExpandedNodeId"/> with a
    /// namespace URI and the index of that Server in the <c>ServerArray</c>.
    /// </para>
    /// <para>
    /// Annex C.3: removing a source removes the aliases and categories only
    /// that source contributed, removes its targets from aliases that other
    /// sources share, and removes its ServerUri from the <c>ServerArray</c>
    /// unless another source still uses it. Removing an entry renumbers the
    /// entries behind it, so the targets of the merged view are resolved
    /// against the <c>ServerArray</c> each time the view is built.
    /// </para>
    /// <para>
    /// The aggregator is registered as a read-only contributor with the
    /// server's <see cref="IAliasNameStoreRegistry"/> (see
    /// <see cref="IAliasNameStoreRegistry.RegisterContributor"/>), so
    /// <c>FindAlias</c> on the well-known categories returns the aggregated
    /// aliases next to any the server defines itself.
    /// </para>
    /// </remarks>
    public sealed class AliasNameAggregator : IAliasNameStore
    {
        /// <summary>
        /// Creates an aggregator.
        /// </summary>
        /// <param name="namespaceIndex">The namespace the aggregated category
        /// and alias nodes, and the alias names, are created in.</param>
        /// <param name="namespaceUris">The server's namespace table; resolves
        /// targets that live on this server.</param>
        /// <param name="serverUris">The server's <c>ServerArray</c> table.</param>
        public AliasNameAggregator(
            ushort namespaceIndex,
            NamespaceTable namespaceUris,
            StringTable serverUris)
        {
            NamespaceIndex = namespaceIndex;
            m_namespaceUris = namespaceUris ?? throw new ArgumentNullException(nameof(namespaceUris));
            m_serverUris = serverUris ?? throw new ArgumentNullException(nameof(serverUris));
        }

        /// <summary>
        /// The namespace of the aggregated nodes and alias names.
        /// </summary>
        public ushort NamespaceIndex { get; }

        /// <summary>
        /// The applications whose AliasNames are aggregated.
        /// </summary>
        public IReadOnlyList<NodeId> Sources
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_snapshots.Keys];
                }
            }
        }

        /// <summary>
        /// Adds or replaces the AliasNames of one registered application.
        /// </summary>
        /// <param name="applicationId">The ApplicationId of the record.</param>
        /// <param name="snapshot">The AliasNames read from the application.</param>
        public void SetSource(NodeId applicationId, AliasNameSourceSnapshot snapshot)
        {
            if (applicationId.IsNull)
            {
                throw new ArgumentException("The application id must be set.", nameof(applicationId));
            }
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            lock (m_lock)
            {
                m_snapshots[applicationId] = snapshot;
                foreach (string serverUri in GetServerUris(snapshot))
                {
                    if (m_serverUris.GetIndex(serverUri) < 0)
                    {
                        m_serverUris.Append(serverUri);
                        m_addedServerUris.Add(serverUri);
                    }
                }
                ReleaseUnusedServerUris();
                m_merged = null;
            }
        }

        /// <summary>
        /// Removes the AliasNames of one application.
        /// </summary>
        /// <param name="applicationId">The ApplicationId of the record.</param>
        /// <returns><c>true</c> if the application was a source.</returns>
        public bool RemoveSource(NodeId applicationId)
        {
            lock (m_lock)
            {
                if (!m_snapshots.Remove(applicationId))
                {
                    return false;
                }
                ReleaseUnusedServerUris();
                m_merged = null;
                return true;
            }
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="applicationId"/> is a source.
        /// </summary>
        public bool ContainsSource(NodeId applicationId)
        {
            lock (m_lock)
            {
                return m_snapshots.ContainsKey(applicationId);
            }
        }

        /// <summary>
        /// Returns the merged view the address space is built from: every
        /// server-defined category (parents before children) and every
        /// alias with its resolved targets.
        /// </summary>
        public AliasNameAggregateView GetView()
        {
            lock (m_lock)
            {
                Merged merged = GetMerged();
                var categories = new List<AliasNameAggregateCategory>(merged.Categories.Count);
                foreach (MergedCategory category in merged.Categories.Values
                    .OrderBy(c => c.Path.Count)
                    .ThenBy(c => c.Key, StringComparer.Ordinal))
                {
                    if (category.IsWellKnown)
                    {
                        continue;
                    }
                    categories.Add(new AliasNameAggregateCategory(
                        category.NodeId,
                        category.Name,
                        category.ParentId));
                }

                var aliases = new List<AliasNameAggregateAlias>(merged.Aliases.Count);
                foreach (MergedAlias alias in merged.Aliases.Values
                    .OrderBy(a => a.Category.Key, StringComparer.Ordinal)
                    .ThenBy(a => a.Name, StringComparer.Ordinal))
                {
                    var targets = new List<AliasNameAggregateTarget>(alias.Targets.Count);
                    foreach (MergedTarget target in alias.Targets)
                    {
                        ExpandedNodeId resolved = Resolve(target);
                        if (!resolved.IsNull)
                        {
                            targets.Add(new AliasNameAggregateTarget(target.ReferenceTypeId, resolved));
                        }
                    }
                    aliases.Add(new AliasNameAggregateAlias(
                        alias.NodeId,
                        alias.Name,
                        alias.Category.NodeId,
                        targets));
                }
                return new AliasNameAggregateView(categories, aliases);
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Empty: the aggregator is a contributor, never the owner of a
        /// category, and its server-defined categories change with the
        /// registrations.
        /// </remarks>
        public IReadOnlyList<AliasNameCategoryDescriptor> RootCategories => [];

        /// <inheritdoc/>
        /// <remarks>
        /// Never raised: the aggregated categories expose no
        /// <c>LastChange</c>, and the owner of the well-known categories
        /// keeps their <c>LastChange</c>.
        /// </remarks>
        public event EventHandler<AliasStoreChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public uint? GetLastChange(NodeId categoryId)
        {
            return null;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The well-known <c>Aliases</c>, <c>TagVariables</c> and
        /// <c>Topics</c> categories always count as aggregated, so a GDS
        /// without registrations answers <c>FindAlias</c> on them with an
        /// empty result.
        /// </remarks>
        public bool OwnsCategory(NodeId categoryId)
        {
            if (IsWellKnownCategory(categoryId))
            {
                return true;
            }
            lock (m_lock)
            {
                return GetMerged().CategoriesById.ContainsKey(categoryId);
            }
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<AliasNameDataType>> FindAliasAsync(
            NodeId categoryId,
            string? aliasNameSearchPattern,
            NodeId referenceTypeFilter,
            ITypeTable typeTree,
            CancellationToken ct = default)
        {
            var result = new List<AliasNameDataType>();
            foreach ((MergedAlias alias, NodeId referenceTypeId, List<MergedTarget> targets) in
                Find(categoryId, aliasNameSearchPattern, referenceTypeFilter, typeTree, ct))
            {
                result.Add(new AliasNameDataType
                {
                    AliasName = new QualifiedName(alias.Name, NamespaceIndex),
                    ReferencedNodes = targets.ConvertAll(ResolveUnlocked).ToArrayOf()
                });
            }
            return new ValueTask<IReadOnlyList<AliasNameDataType>>(result);
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<AliasNameVerboseDataType>> FindAliasVerboseAsync(
            NodeId categoryId,
            string? aliasNameSearchPattern,
            NodeId referenceTypeFilter,
            ITypeTable typeTree,
            CancellationToken ct = default)
        {
            var result = new List<AliasNameVerboseDataType>();
            foreach ((MergedAlias alias, NodeId referenceTypeId, List<MergedTarget> targets) in
                Find(categoryId, aliasNameSearchPattern, referenceTypeFilter, typeTree, ct))
            {
                result.Add(new AliasNameVerboseDataType
                {
                    AliasName = new QualifiedName(alias.Name, NamespaceIndex),
                    ReferencedNodes = targets.ConvertAll(ResolveUnlocked).ToArrayOf(),
                    ServerUris = targets.ConvertAll(t => t.ServerUri).ToArrayOf(),
                    AliasNameCategoryId = alias.Category.NodeId
                });
            }
            return new ValueTask<IReadOnlyList<AliasNameVerboseDataType>>(result);
        }

        /// <inheritdoc/>
        /// <exception cref="ServiceResultException">Always: aggregated
        /// aliases are maintained by the registered Servers.</exception>
        public ValueTask<StatusCode[]> AddAliasesAsync(
            NodeId categoryId,
            IReadOnlyList<AliasAddRequest> requests,
            CancellationToken ct = default)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        /// <exception cref="ServiceResultException">Always: aggregated
        /// aliases are maintained by the registered Servers.</exception>
        public ValueTask<StatusCode[]> DeleteAliasesAsync(
            NodeId categoryId,
            IReadOnlyList<AliasDeleteRequest> requests,
            CancellationToken ct = default)
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported);
        }

        /// <summary>
        /// Returns <c>true</c> for the well-known <c>Aliases</c>,
        /// <c>TagVariables</c> and <c>Topics</c> categories.
        /// </summary>
        public static bool IsWellKnownCategory(NodeId categoryId)
        {
            return categoryId == Ua.ObjectIds.Aliases ||
                categoryId == Ua.ObjectIds.TagVariables ||
                categoryId == Ua.ObjectIds.Topics;
        }

        private List<(MergedAlias Alias, NodeId ReferenceTypeId, List<MergedTarget> Targets)> Find(
            NodeId categoryId,
            string? aliasNameSearchPattern,
            NodeId referenceTypeFilter,
            ITypeTable typeTree,
            CancellationToken ct)
        {
            var result = new List<(MergedAlias, NodeId, List<MergedTarget>)>();
            if (string.IsNullOrEmpty(aliasNameSearchPattern))
            {
                return result;
            }
            if (!LikePattern.TryParse(aliasNameSearchPattern, out LikePattern? pattern))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument, "Invalid alias-name search pattern.");
            }

            lock (m_lock)
            {
                Merged merged = GetMerged();
                IReadOnlyList<string>? scope = null;
                if (merged.CategoriesById.TryGetValue(categoryId, out MergedCategory? category))
                {
                    scope = category.Path;
                }
                else if (IsWellKnownCategory(categoryId))
                {
                    scope = WellKnownPath(categoryId);
                }
                if (scope == null)
                {
                    return result;
                }

                foreach (MergedAlias alias in merged.Aliases.Values)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!StartsWith(alias.Category.Path, scope) ||
                        !pattern.IsMatch(alias.Name, s_matchTimeout))
                    {
                        continue;
                    }

                    // One record per alias and reference type, as the
                    // server's own stores report them.
                    foreach (IGrouping<NodeId, MergedTarget> group in alias.Targets
                        .Where(t => MatchesReferenceTypeFilter(t.ReferenceTypeId, referenceTypeFilter, typeTree))
                        .GroupBy(t => t.ReferenceTypeId))
                    {
                        result.Add((alias, group.Key, [.. group]));
                    }
                }
            }
            return result;
        }

        private static bool MatchesReferenceTypeFilter(
            NodeId referenceTypeId,
            NodeId filter,
            ITypeTable typeTree)
        {
            if (filter.IsNull || filter == ReferenceTypeIds.References || referenceTypeId == filter)
            {
                return true;
            }
            return typeTree != null && typeTree.IsTypeOf(referenceTypeId, filter);
        }

        private ExpandedNodeId ResolveUnlocked(MergedTarget target)
        {
            lock (m_lock)
            {
                return Resolve(target);
            }
        }

        /// <summary>
        /// Turns a target into the form the address space and
        /// <c>FindAlias</c> return: a local NodeId for a target on this
        /// server, otherwise the namespace URI form with the Server's index
        /// in the <c>ServerArray</c>.
        /// </summary>
        private ExpandedNodeId Resolve(MergedTarget target)
        {
            int serverIndex = m_serverUris.GetIndex(target.ServerUri);
            if (serverIndex == 0)
            {
                NodeId local = ExpandedNodeId.ToNodeId(target.Target, m_namespaceUris);
                return local.IsNull ? target.Target : new ExpandedNodeId(local);
            }
            if (serverIndex < 0)
            {
                return ExpandedNodeId.Null;
            }
            return target.Target.WithServerIndex((uint)serverIndex);
        }

        private Merged GetMerged()
        {
            return m_merged ??= BuildMerged();
        }

        private Merged BuildMerged()
        {
            var merged = new Merged();
            foreach (AliasNameSourceSnapshot snapshot in m_snapshots.Values)
            {
                foreach (AliasNameSourceCategory category in snapshot.Categories)
                {
                    GetOrAddCategory(merged, category.Path);
                }
                foreach (AliasNameSourceAlias alias in snapshot.Aliases)
                {
                    if (string.IsNullOrEmpty(alias.Name) || alias.Target.IsNull)
                    {
                        continue;
                    }
                    MergedCategory category = GetOrAddCategory(merged, alias.CategoryPath);
                    string key = category.Key + "#" + Escape(alias.Name);
                    if (!merged.Aliases.TryGetValue(key, out MergedAlias? entry))
                    {
                        entry = new MergedAlias(
                            new NodeId(Prefix + key, NamespaceIndex),
                            alias.Name,
                            category);
                        merged.Aliases.Add(key, entry);
                    }
                    var target = new MergedTarget(alias.ReferenceTypeId, alias.Target, alias.ServerUri);
                    if (!entry.Targets.Contains(target))
                    {
                        entry.Targets.Add(target);
                    }
                }
            }
            return merged;
        }

        private MergedCategory GetOrAddCategory(Merged merged, IReadOnlyList<string> path)
        {
            string key = KeyOf(path);
            if (merged.Categories.TryGetValue(key, out MergedCategory? category))
            {
                return category;
            }

            NodeId parentId = NodeId.Null;
            if (path.Count > 0)
            {
                parentId = GetOrAddCategory(merged, [.. path.Take(path.Count - 1)]).NodeId;
            }

            NodeId nodeId = WellKnownId(path);
            bool isWellKnown = !nodeId.IsNull;
            if (!isWellKnown)
            {
                nodeId = new NodeId(Prefix + key, NamespaceIndex);
            }
            category = new MergedCategory(
                key,
                [.. path],
                path.Count > 0 ? path[path.Count - 1] : Ua.BrowseNames.Aliases,
                nodeId,
                parentId,
                isWellKnown);
            merged.Categories.Add(key, category);
            merged.CategoriesById[nodeId] = category;
            return category;
        }

        private static NodeId WellKnownId(IReadOnlyList<string> path)
        {
            if (path.Count == 0)
            {
                return Ua.ObjectIds.Aliases;
            }
            if (path.Count == 1)
            {
                if (path[0] == Ua.BrowseNames.TagVariables)
                {
                    return Ua.ObjectIds.TagVariables;
                }
                if (path[0] == Ua.BrowseNames.Topics)
                {
                    return Ua.ObjectIds.Topics;
                }
            }
            return NodeId.Null;
        }

        private static IReadOnlyList<string> WellKnownPath(NodeId categoryId)
        {
            if (categoryId == Ua.ObjectIds.TagVariables)
            {
                return [Ua.BrowseNames.TagVariables];
            }
            if (categoryId == Ua.ObjectIds.Topics)
            {
                return [Ua.BrowseNames.Topics];
            }
            return [];
        }

        private static bool StartsWith(IReadOnlyList<string> path, IReadOnlyList<string> prefix)
        {
            if (path.Count < prefix.Count)
            {
                return false;
            }
            for (int ii = 0; ii < prefix.Count; ii++)
            {
                if (!string.Equals(path[ii], prefix[ii], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static HashSet<string> GetServerUris(AliasNameSourceSnapshot snapshot)
        {
            var uris = new HashSet<string>(StringComparer.Ordinal) { snapshot.ServerUri };
            foreach (AliasNameSourceAlias alias in snapshot.Aliases)
            {
                if (!string.IsNullOrEmpty(alias.ServerUri))
                {
                    uris.Add(alias.ServerUri);
                }
            }
            return uris;
        }

        /// <summary>
        /// Removes the ServerUris this aggregator added and no source uses
        /// any more from the <c>ServerArray</c> (Part 17 Annex C.3).
        /// </summary>
        private void ReleaseUnusedServerUris()
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (AliasNameSourceSnapshot snapshot in m_snapshots.Values)
            {
                used.UnionWith(GetServerUris(snapshot));
            }

            List<string> unused = [.. m_addedServerUris.Where(uri => !used.Contains(uri))];
            if (unused.Count == 0)
            {
                return;
            }
            foreach (string uri in unused)
            {
                m_addedServerUris.Remove(uri);
            }
            string[] current = m_serverUris.ToArray();
            m_serverUris.Update(current.Where((uri, index) => index == 0 || !unused.Contains(uri)));
        }

        private static string KeyOf(IReadOnlyList<string> path)
        {
            var builder = new StringBuilder();
            foreach (string segment in path)
            {
                builder.Append('/').Append(Escape(segment));
            }
            return builder.ToString();
        }

        private static string Escape(string segment)
        {
            var builder = new StringBuilder(segment.Length);
            foreach (char c in segment)
            {
                switch (c)
                {
                    case '%':
                        builder.Append("%25");
                        break;
                    case '/':
                        builder.Append("%2F");
                        break;
                    case '#':
                        builder.Append("%23");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }
            return builder.ToString();
        }

        private sealed class Merged
        {
            public Dictionary<string, MergedCategory> Categories { get; } = new(StringComparer.Ordinal);
            public Dictionary<NodeId, MergedCategory> CategoriesById { get; } = [];
            public Dictionary<string, MergedAlias> Aliases { get; } = new(StringComparer.Ordinal);
        }

        private sealed record MergedCategory(
            string Key,
            IReadOnlyList<string> Path,
            string Name,
            NodeId NodeId,
            NodeId ParentId,
            bool IsWellKnown);

        private sealed record MergedAlias(NodeId NodeId, string Name, MergedCategory Category)
        {
            public List<MergedTarget> Targets { get; } = [];
        }

        private readonly record struct MergedTarget(
            NodeId ReferenceTypeId,
            ExpandedNodeId Target,
            string ServerUri);

        /// <summary>
        /// Prefix of the string identifiers of the aggregated nodes; the
        /// rest is the escaped category path and, for an alias, its name.
        /// </summary>
        private const string Prefix = "AliasNames";

        private static readonly TimeSpan s_matchTimeout = TimeSpan.FromMilliseconds(100);
        private readonly Lock m_lock = new();
        private readonly NamespaceTable m_namespaceUris;
        private readonly StringTable m_serverUris;
        private readonly Dictionary<NodeId, AliasNameSourceSnapshot> m_snapshots = [];
        private readonly HashSet<string> m_addedServerUris = new(StringComparer.Ordinal);
        private Merged? m_merged;
    }

    /// <summary>
    /// The merged AliasNames of all sources (see
    /// <see cref="AliasNameAggregator.GetView"/>).
    /// </summary>
    /// <param name="Categories">The server-defined categories, parents first.</param>
    /// <param name="Aliases">The aliases.</param>
    public sealed record AliasNameAggregateView(
        IReadOnlyList<AliasNameAggregateCategory> Categories,
        IReadOnlyList<AliasNameAggregateAlias> Aliases);

    /// <summary>
    /// An aggregated server-defined category.
    /// </summary>
    /// <param name="NodeId">The NodeId of the category node.</param>
    /// <param name="Name">The Name part of the BrowseName.</param>
    /// <param name="ParentId">The category that organizes it.</param>
    public sealed record AliasNameAggregateCategory(NodeId NodeId, string Name, NodeId ParentId);

    /// <summary>
    /// An aggregated alias.
    /// </summary>
    /// <param name="NodeId">The NodeId of the alias node.</param>
    /// <param name="Name">The alias name.</param>
    /// <param name="CategoryId">The category that organizes it.</param>
    /// <param name="Targets">The merged targets.</param>
    public sealed record AliasNameAggregateAlias(
        NodeId NodeId,
        string Name,
        NodeId CategoryId,
        IReadOnlyList<AliasNameAggregateTarget> Targets);

    /// <summary>
    /// One reference from an aggregated alias to its target.
    /// </summary>
    /// <param name="ReferenceTypeId">AliasFor or a subtype.</param>
    /// <param name="Target">The target, with the ServerArray index of the
    /// Server that hosts it.</param>
    public readonly record struct AliasNameAggregateTarget(
        NodeId ReferenceTypeId,
        ExpandedNodeId Target);
}
