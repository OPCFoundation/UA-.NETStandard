/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Thread-safe in-memory implementation of <see cref="IAliasNameStore"/>
    /// suitable for development, tests and small read-mostly deployments.
    /// Mutations are serialized through a <see cref="SemaphoreSlim"/> to
    /// match the repo's preference for async-friendly locking over the
    /// <c>lock</c> keyword.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aliases inside a category are keyed by case-sensitive name. Multiple
    /// targets for the same name are stored as parallel mapping entries
    /// distinguished by <c>(TargetReferenceType, TargetNode)</c>. This
    /// matches the wire shape of Part 17 §6.3.4
    /// (<c>AddAliasesToCategory</c>) where each (name, target) pair is its
    /// own input row.
    /// </para>
    /// <para>
    /// <c>FindAlias</c> /<c>FindAliasVerbose</c> aggregate matching entries
    /// back into one <c>AliasNameDataType</c> record per unique
    /// <c>(name, ReferenceType)</c> tuple — when a single alias name has
    /// targets reached through different reference types, the result
    /// contains one record per reference type bucket (per Part 17 §7.2).
    /// </para>
    /// </remarks>
    public sealed class InMemoryAliasNameStore : IAliasNameStore, IDisposable
    {
        /// <summary>
        /// Initializes a new in-memory store backed by the supplied
        /// category descriptors.
        /// </summary>
        /// <param name="rootCategories">The root categories the store
        /// serves; sub-categories are registered recursively.</param>
        public InMemoryAliasNameStore(
            IReadOnlyList<AliasNameCategoryDescriptor> rootCategories)
        {
            RootCategories = rootCategories ?? throw new ArgumentNullException(nameof(rootCategories));

            // Flatten the descriptor tree into a category lookup table.
            foreach (AliasNameCategoryDescriptor root in rootCategories)
            {
                RegisterCategoryRecursive(root);
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<AliasNameCategoryDescriptor> RootCategories { get; }

        /// <inheritdoc/>
        public event EventHandler<AliasStoreChangedEventArgs>? Changed;

        /// <inheritdoc/>
        public bool OwnsCategory(NodeId categoryId)
        {
            return !categoryId.IsNull && m_categories.ContainsKey(categoryId);
        }

        /// <inheritdoc/>
        public uint? GetLastChange(NodeId categoryId)
        {
            if (categoryId.IsNull ||
                !m_categories.TryGetValue(categoryId, out CategoryEntry? entry))
            {
                return null;
            }
            if ((entry.Descriptor.Capabilities & AliasNameCapabilities.LastChange) == 0)
            {
                return null;
            }
            return entry.LastChange;
        }

        /// <summary>
        /// Synchronously seeds an alias mapping at construction or fixture
        /// time. Bypasses <c>LastChange</c> bumps and the <c>Changed</c>
        /// event. Throws when the category is unknown.
        /// </summary>
        /// <param name="categoryId">Target category.</param>
        /// <param name="name">Alias name.</param>
        /// <param name="targetNode">Target node.</param>
        /// <param name="serverUri">Optional server URI; <c>null</c> means
        /// the local server.</param>
        /// <param name="referenceTypeId">Reference type — typically
        /// <c>ReferenceTypeIds.AliasFor</c>.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="name"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="categoryId"/> does not match a registered
        /// category.
        /// </exception>
        public void Seed(
            NodeId categoryId,
            string name,
            ExpandedNodeId targetNode,
            string? serverUri,
            NodeId referenceTypeId)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            if (!m_categories.TryGetValue(categoryId, out CategoryEntry? entry))
            {
                throw new ArgumentException(
                    "Unknown category: " + categoryId,
                    nameof(categoryId));
            }
            var key = new MappingKey(referenceTypeId, targetNode);
            if (!entry.Aliases.TryGetValue(name, out Dictionary<MappingKey, string?>? group))
            {
                group = [];
                entry.Aliases[name] = group;
            }
            group[key] = serverUri;
        }

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<AliasNameDataType>> FindAliasAsync(
            NodeId categoryId,
            string? aliasNameSearchPattern,
            NodeId referenceTypeFilter,
            ITypeTable typeTree,
            CancellationToken ct = default)
        {
            if (typeTree == null)
            {
                throw new ArgumentNullException(nameof(typeTree));
            }

            var result = new List<AliasNameDataType>();
            if (string.IsNullOrEmpty(aliasNameSearchPattern))
            {
                return result;
            }
            if (!m_categories.TryGetValue(categoryId, out CategoryEntry? root))
            {
                return result;
            }

            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                CollectMatches(
                    root,
                    aliasNameSearchPattern!,
                    referenceTypeFilter,
                    typeTree,
                    verbose: false,
                    nonVerboseSink: result,
                    verboseSink: null);
            }
            finally
            {
                m_semaphore.Release();
            }
            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<IReadOnlyList<AliasNameVerboseDataType>> FindAliasVerboseAsync(
            NodeId categoryId,
            string? aliasNameSearchPattern,
            NodeId referenceTypeFilter,
            ITypeTable typeTree,
            CancellationToken ct = default)
        {
            if (typeTree == null)
            {
                throw new ArgumentNullException(nameof(typeTree));
            }

            var result = new List<AliasNameVerboseDataType>();
            if (string.IsNullOrEmpty(aliasNameSearchPattern))
            {
                return result;
            }
            if (!m_categories.TryGetValue(categoryId, out CategoryEntry? root))
            {
                return result;
            }

            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                CollectMatches(
                    root,
                    aliasNameSearchPattern!,
                    referenceTypeFilter,
                    typeTree,
                    verbose: true,
                    nonVerboseSink: null,
                    verboseSink: result);
            }
            finally
            {
                m_semaphore.Release();
            }
            return result;
        }

        /// <inheritdoc/>
        public async ValueTask<StatusCode[]> AddAliasesAsync(
            NodeId categoryId,
            IReadOnlyList<AliasAddRequest> requests,
            CancellationToken ct = default)
        {
            if (requests == null)
            {
                throw new ArgumentNullException(nameof(requests));
            }
            if (!m_categories.TryGetValue(categoryId, out CategoryEntry? entry))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "Category not found: {0}",
                    categoryId);
            }
            if ((entry.Descriptor.Capabilities & AliasNameCapabilities.AddAliasesToCategory) == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "Category {0} does not support AddAliasesToCategory.",
                    categoryId);
            }

            var results = new StatusCode[requests.Count];
            if (requests.Count == 0)
            {
                return results;
            }

            bool changed = false;
            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                for (int i = 0; i < requests.Count; i++)
                {
                    AliasAddRequest req = requests[i];
                    if (string.IsNullOrEmpty(req.Name))
                    {
                        results[i] = StatusCodes.BadBrowseNameInvalid;
                        continue;
                    }
                    if (req.TargetNode.IsNull)
                    {
                        results[i] = StatusCodes.BadNodeIdInvalid;
                        continue;
                    }
                    if (req.TargetReferenceType.IsNull)
                    {
                        results[i] = StatusCodes.BadReferenceTypeIdInvalid;
                        continue;
                    }

                    var key = new MappingKey(req.TargetReferenceType, req.TargetNode);
                    if (!entry.Aliases.TryGetValue(req.Name,
                            out Dictionary<MappingKey, string?>? group))
                    {
                        group = [];
                        entry.Aliases[req.Name] = group;
                    }
                    if (group.ContainsKey(key))
                    {
                        results[i] = StatusCodes.BadBrowseNameDuplicated;
                        continue;
                    }
                    group[key] = string.IsNullOrEmpty(req.TargetServer)
                        ? null
                        : req.TargetServer;
                    results[i] = StatusCodes.Good;
                    changed = true;
                }

                if (changed)
                {
                    entry.LastChange = unchecked(entry.LastChange + 1);
                }
            }
            finally
            {
                m_semaphore.Release();
            }

            if (changed)
            {
                Changed?.Invoke(this,
                    new AliasStoreChangedEventArgs(categoryId, entry.LastChange));
            }
            return results;
        }

        /// <inheritdoc/>
        public async ValueTask<StatusCode[]> DeleteAliasesAsync(
            NodeId categoryId,
            IReadOnlyList<AliasDeleteRequest> requests,
            CancellationToken ct = default)
        {
            if (requests == null)
            {
                throw new ArgumentNullException(nameof(requests));
            }
            if (!m_categories.TryGetValue(categoryId, out CategoryEntry? entry))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "Category not found: {0}",
                    categoryId);
            }
            if ((entry.Descriptor.Capabilities & AliasNameCapabilities.DeleteAliasesFromCategory) == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "Category {0} does not support DeleteAliasesFromCategory.",
                    categoryId);
            }

            var results = new StatusCode[requests.Count];
            if (requests.Count == 0)
            {
                return results;
            }

            bool changed = false;
            await m_semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                for (int i = 0; i < requests.Count; i++)
                {
                    AliasDeleteRequest req = requests[i];
                    if (string.IsNullOrEmpty(req.Name))
                    {
                        results[i] = StatusCodes.BadBrowseNameInvalid;
                        continue;
                    }
                    if (req.TargetNode.IsNull)
                    {
                        results[i] = StatusCodes.BadNodeIdInvalid;
                        continue;
                    }
                    if (!entry.Aliases.TryGetValue(req.Name,
                            out Dictionary<MappingKey, string?>? group))
                    {
                        results[i] = StatusCodes.BadNotFound;
                        continue;
                    }
                    bool removed = false;
                    // The wire signature does not include a reference type so
                    // we remove every reference-type bucket whose target
                    // matches; the typical case has a single bucket.
                    var keysToRemove = new List<MappingKey>();
                    foreach (KeyValuePair<MappingKey, string?> kv in group)
                    {
                        if (kv.Key.TargetNode == req.TargetNode)
                        {
                            keysToRemove.Add(kv.Key);
                        }
                    }
                    foreach (MappingKey k in keysToRemove)
                    {
                        group.Remove(k);
                        removed = true;
                    }
                    if (!removed)
                    {
                        results[i] = StatusCodes.BadNotFound;
                        continue;
                    }
                    if (group.Count == 0)
                    {
                        entry.Aliases.Remove(req.Name);
                    }
                    results[i] = StatusCodes.Good;
                    changed = true;
                }

                if (changed)
                {
                    entry.LastChange = unchecked(entry.LastChange + 1);
                }
            }
            finally
            {
                m_semaphore.Release();
            }

            if (changed)
            {
                Changed?.Invoke(this,
                    new AliasStoreChangedEventArgs(categoryId, entry.LastChange));
            }
            return results;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_semaphore.Dispose();
        }

        private void RegisterCategoryRecursive(AliasNameCategoryDescriptor descriptor)
        {
            if (!m_categories.TryAdd(descriptor.NodeId, new CategoryEntry(descriptor)))
            {
                throw new ArgumentException(
                    "Duplicate category NodeId: " + descriptor.NodeId,
                    nameof(descriptor));
            }
            foreach (AliasNameCategoryDescriptor child in descriptor.SubCategories)
            {
                RegisterCategoryRecursive(child);
            }
        }

        private void CollectMatches(
            CategoryEntry category,
            string pattern,
            NodeId referenceTypeFilter,
            ITypeTable typeTree,
            bool verbose,
            List<AliasNameDataType>? nonVerboseSink,
            List<AliasNameVerboseDataType>? verboseSink)
        {
            ushort nsIndex = category.Descriptor.BrowseName.NamespaceIndex;

            foreach (KeyValuePair<string, Dictionary<MappingKey, string?>> alias
                in category.Aliases)
            {
                if (!AliasNameWildcardMatcher.IsMatch(alias.Key, pattern))
                {
                    continue;
                }

                // Group surviving (post-filter) targets by reference type so
                // that the wire result has one record per (name, refType).
                Dictionary<NodeId, List<KeyValuePair<MappingKey, string?>>> byRefType = [];
                foreach (KeyValuePair<MappingKey, string?> mapping in alias.Value)
                {
                    if (!MatchesReferenceTypeFilter(
                            mapping.Key.ReferenceTypeId,
                            referenceTypeFilter,
                            typeTree))
                    {
                        continue;
                    }
                    if (!byRefType.TryGetValue(mapping.Key.ReferenceTypeId,
                            out List<KeyValuePair<MappingKey, string?>>? bucket))
                    {
                        bucket = [];
                        byRefType[mapping.Key.ReferenceTypeId] = bucket;
                    }
                    bucket.Add(mapping);
                }

                foreach (List<KeyValuePair<MappingKey, string?>> bucket in byRefType.Values)
                {
                    if (bucket.Count == 0)
                    {
                        continue;
                    }
                    var aliasName = new QualifiedName(alias.Key, nsIndex);
                    if (verbose)
                    {
                        verboseSink!.Add(BuildVerbose(aliasName, category.Descriptor.NodeId, bucket));
                    }
                    else
                    {
                        nonVerboseSink!.Add(BuildNonVerbose(aliasName, bucket));
                    }
                }
            }

            // Recurse into sub-categories.
            foreach (AliasNameCategoryDescriptor child in category.Descriptor.SubCategories)
            {
                if (m_categories.TryGetValue(child.NodeId, out CategoryEntry? childEntry))
                {
                    CollectMatches(
                        childEntry,
                        pattern,
                        referenceTypeFilter,
                        typeTree,
                        verbose,
                        nonVerboseSink,
                        verboseSink);
                }
            }
        }

        private static AliasNameDataType BuildNonVerbose(
            QualifiedName name,
            List<KeyValuePair<MappingKey, string?>> bucket)
        {
            var targets = new ExpandedNodeId[bucket.Count];
            for (int i = 0; i < bucket.Count; i++)
            {
                targets[i] = bucket[i].Key.TargetNode;
            }
            return new AliasNameDataType
            {
                AliasName = name,
                ReferencedNodes = targets.ToArrayOf()
            };
        }

        private static AliasNameVerboseDataType BuildVerbose(
            QualifiedName name,
            NodeId categoryId,
            List<KeyValuePair<MappingKey, string?>> bucket)
        {
            var targets = new ExpandedNodeId[bucket.Count];
            string[] serverUris = new string[bucket.Count];
            for (int i = 0; i < bucket.Count; i++)
            {
                targets[i] = bucket[i].Key.TargetNode;
                serverUris[i] = bucket[i].Value ?? string.Empty;
            }
            return new AliasNameVerboseDataType
            {
                AliasName = name,
                ReferencedNodes = targets.ToArrayOf(),
                ServerUris = serverUris.ToArrayOf(),
                AliasNameCategoryId = categoryId
            };
        }

        private static bool MatchesReferenceTypeFilter(
            NodeId aliasRefType,
            NodeId filter,
            ITypeTable typeTree)
        {
            // Null/empty/References → match every alias regardless of refType.
            if (filter.IsNull ||
                filter.Equals(ReferenceTypeIds.References))
            {
                return true;
            }
            if (aliasRefType.Equals(filter))
            {
                return true;
            }
            return typeTree.IsTypeOf(aliasRefType, filter);
        }

        private readonly Dictionary<NodeId, CategoryEntry> m_categories = [];
        private readonly SemaphoreSlim m_semaphore = new(1, 1);

        private readonly record struct MappingKey(
            NodeId ReferenceTypeId,
            ExpandedNodeId TargetNode);

        private sealed class CategoryEntry
        {
            public CategoryEntry(AliasNameCategoryDescriptor descriptor)
            {
                Descriptor = descriptor;
                Aliases = [];
                LastChange = 0;
            }

            public AliasNameCategoryDescriptor Descriptor { get; }
            public Dictionary<string, Dictionary<MappingKey, string?>> Aliases { get; }
            public uint LastChange { get; set; }
        }
    }
}
