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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.AliasNames
{
    /// <summary>
    /// High-level async client over an OPC UA Part 17
    /// <c>AliasNameCategoryType</c> instance — supports
    /// <c>FindAlias</c> (§6.3.2), <c>FindAliasVerbose</c> (§6.3.3),
    /// <c>AddAliasesToCategory</c> (§6.3.4),
    /// <c>DeleteAliasesFromCategory</c> (§6.3.5),
    /// <c>LastChange</c> (§6.3.1) and sub-category enumeration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client lazily resolves method NodeIds via
    /// <c>TranslateBrowsePathsToNodeIds</c> against the category root and
    /// caches them — so every method call after the first only costs one
    /// <c>Call</c> service round-trip. Standard well-known categories
    /// (<c>Aliases</c>, <c>TagVariables</c>, <c>Topics</c>) use
    /// hardcoded method NodeIds as a fast-path.
    /// </para>
    /// <para>
    /// Per-entry Part 17 status codes are returned as a
    /// <see cref="StatusCode"/> array. Method-level failures
    /// (<c>BadUserAccessDenied</c>, <c>BadNotImplemented</c>, …)
    /// are translated into typed exceptions
    /// (<see cref="UnauthorizedAccessException"/>,
    /// <see cref="NotSupportedException"/>) when raised at the service
    /// level.
    /// </para>
    /// </remarks>
    public sealed class AliasNameClient
    {
        /// <summary>
        /// Initializes a new client rooted at the supplied
        /// <c>AliasNameCategoryType</c> instance.
        /// </summary>
        /// <param name="session">The OPC UA session used for all service
        /// calls.</param>
        /// <param name="categoryId">The <see cref="NodeId"/> of the
        /// <c>AliasNameCategoryType</c> instance.</param>
        /// <param name="options">Optional configuration; defaults applied
        /// when <c>null</c>.</param>
        public AliasNameClient(
            ISession session,
            NodeId categoryId,
            AliasNameClientOptions? options = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            if (categoryId.IsNull)
            {
                throw new ArgumentException(
                    "categoryId must not be null.",
                    nameof(categoryId));
            }
            CategoryId = categoryId;
            Options = (options ?? new AliasNameClientOptions()).Clone();
            m_methodIdCache = StandardMethodIdCache.For(categoryId);
        }

        /// <summary>
        /// Opens an <see cref="AliasNameClient"/> rooted at the standard
        /// well-known <c>Aliases (i=23470)</c> object (Part 17 §9.2).
        /// </summary>
        public static AliasNameClient OpenStandardAliases(
            ISession session,
            AliasNameClientOptions? options = null)
        {
            return new AliasNameClient(session, ObjectIds.Aliases, options);
        }

        /// <summary>
        /// Opens an <see cref="AliasNameClient"/> rooted at the standard
        /// well-known <c>TagVariables (i=23479)</c> category (Part 17 §9.3).
        /// </summary>
        public static AliasNameClient OpenStandardTagVariables(
            ISession session,
            AliasNameClientOptions? options = null)
        {
            return new AliasNameClient(session, ObjectIds.TagVariables, options);
        }

        /// <summary>
        /// Opens an <see cref="AliasNameClient"/> rooted at the standard
        /// well-known <c>Topics (i=23488)</c> category (Part 17 §9.4).
        /// </summary>
        public static AliasNameClient OpenStandardTopics(
            ISession session,
            AliasNameClientOptions? options = null)
        {
            return new AliasNameClient(session, ObjectIds.Topics, options);
        }

        /// <summary>The session used for all service calls.</summary>
        public ISession Session { get; }

        /// <summary>The category NodeId.</summary>
        public NodeId CategoryId { get; }

        /// <summary>The (cloned, immutable) configuration.</summary>
        public AliasNameClientOptions Options { get; }

        /// <summary>
        /// Calls <c>FindAlias</c> (Part 17 §6.3.2). An empty
        /// <paramref name="aliasNameSearchPattern"/> returns an empty
        /// result; a null/empty <paramref name="referenceTypeFilter"/>
        /// matches every reference type.
        /// </summary>
        public async Task<IReadOnlyList<AliasNameDataType>> FindAliasAsync(
            string aliasNameSearchPattern,
            NodeId? referenceTypeFilter = null,
            CancellationToken ct = default)
        {
            NodeId methodId = await ResolveMethodIdAsync(
                BrowseNames.FindAlias, ct).ConfigureAwait(false);

            ArrayOf<Variant> output = await Session.CallAsync(
                CategoryId,
                methodId,
                ct,
                new Variant(aliasNameSearchPattern ?? string.Empty),
                new Variant(referenceTypeFilter ?? NodeId.Null))
                .ConfigureAwait(false);

            return ExtractAliasNameDataTypeList(output);
        }

        /// <summary>
        /// Calls <c>FindAliasVerbose</c> (Part 17 §6.3.3). Throws
        /// <see cref="NotSupportedException"/> when the category does not
        /// expose this optional method (unless
        /// <see cref="AliasNameClientOptions.AllowVerboseProbe"/> is set,
        /// in which case the call is made and a service-level
        /// <c>BadNotImplemented</c> bubbles up as
        /// <see cref="NotSupportedException"/>).
        /// </summary>
        public async Task<IReadOnlyList<AliasNameVerboseDataType>> FindAliasVerboseAsync(
            string aliasNameSearchPattern,
            NodeId? referenceTypeFilter = null,
            CancellationToken ct = default)
        {
            NodeId methodId;
            try
            {
                methodId = await ResolveMethodIdAsync(
                    BrowseNames.FindAliasVerbose, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotFound
                    && !Options.AllowVerboseProbe)
            {
                throw new NotSupportedException(
                    "Category " + CategoryId +
                    " does not expose the optional FindAliasVerbose method.");
            }

            try
            {
                ArrayOf<Variant> output = await Session.CallAsync(
                    CategoryId,
                    methodId,
                    ct,
                    new Variant(aliasNameSearchPattern ?? string.Empty),
                    new Variant(referenceTypeFilter ?? NodeId.Null))
                    .ConfigureAwait(false);
                return ExtractAliasNameVerboseDataTypeList(output);
            }
            catch (ServiceResultException sre)
            {
                throw AliasNameClientErrors.Translate(
                    sre.StatusCode, "FindAliasVerbose", CategoryId);
            }
        }

        /// <summary>
        /// Calls <c>AddAliasesToCategory</c> (Part 17 §6.3.4).
        /// </summary>
        /// <returns>One <see cref="StatusCode"/> per input request, in
        /// input order, indicating the per-entry outcome.</returns>
        /// <exception cref="NotSupportedException">The category does not
        /// expose <c>AddAliasesToCategory</c>.</exception>
        /// <exception cref="UnauthorizedAccessException">The session does
        /// not have permission to add aliases to this category.</exception>
        public async Task<StatusCode[]> AddAliasesToCategoryAsync(
            IReadOnlyList<AliasNameAddRequest> requests,
            CancellationToken ct = default)
        {
            if (requests == null)
            {
                throw new ArgumentNullException(nameof(requests));
            }
            NodeId methodId;
            try
            {
                methodId = await ResolveMethodIdAsync(
                    BrowseNames.AddAliasesToCategory, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotFound)
            {
                throw new NotSupportedException(
                    "Category " + CategoryId +
                    " does not expose the optional AddAliasesToCategory method.");
            }

            var names = new string[requests.Count];
            var targets = new ExpandedNodeId[requests.Count];
            var servers = new string[requests.Count];
            NodeId refType = NodeId.Null;
            for (int i = 0; i < requests.Count; i++)
            {
                AliasNameAddRequest req = requests[i];
                names[i] = req.Name ?? string.Empty;
                targets[i] = req.TargetNode;
                servers[i] = req.TargetServer ?? string.Empty;
                if (refType.IsNull)
                {
                    refType = req.TargetReferenceType;
                }
                else if (!refType.Equals(req.TargetReferenceType))
                {
                    throw new ArgumentException(
                        "All AliasNameAddRequest entries in a single call " +
                        "must share the same TargetReferenceType (Part 17 " +
                        "§6.3.4 — TargetReferenceType is a scalar input).",
                        nameof(requests));
                }
            }
            if (refType.IsNull)
            {
                refType = ReferenceTypeIds.AliasFor;
            }

            try
            {
                ArrayOf<Variant> output = await Session.CallAsync(
                    CategoryId,
                    methodId,
                    ct,
                    Variant.From(names.ToArrayOf()),
                    Variant.From(targets.ToArrayOf()),
                    Variant.From(servers.ToArrayOf()),
                    new Variant(refType))
                    .ConfigureAwait(false);
                return ExtractStatusCodeArray(output);
            }
            catch (ServiceResultException sre)
            {
                throw AliasNameClientErrors.Translate(
                    sre.StatusCode, "AddAliasesToCategory", CategoryId);
            }
        }

        /// <summary>
        /// Calls <c>DeleteAliasesFromCategory</c> (Part 17 §6.3.5).
        /// </summary>
        /// <returns>One <see cref="StatusCode"/> per input request, in
        /// input order.</returns>
        /// <exception cref="NotSupportedException">The category does not
        /// expose <c>DeleteAliasesFromCategory</c>.</exception>
        /// <exception cref="UnauthorizedAccessException">The session does
        /// not have permission to delete aliases from this category.</exception>
        public async Task<StatusCode[]> DeleteAliasesFromCategoryAsync(
            IReadOnlyList<AliasNameDeleteRequest> requests,
            CancellationToken ct = default)
        {
            if (requests == null)
            {
                throw new ArgumentNullException(nameof(requests));
            }
            NodeId methodId;
            try
            {
                methodId = await ResolveMethodIdAsync(
                    BrowseNames.DeleteAliasesFromCategory, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotFound)
            {
                throw new NotSupportedException(
                    "Category " + CategoryId +
                    " does not expose the optional " +
                    "DeleteAliasesFromCategory method.");
            }

            var names = new string[requests.Count];
            var targets = new ExpandedNodeId[requests.Count];
            for (int i = 0; i < requests.Count; i++)
            {
                AliasNameDeleteRequest req = requests[i];
                names[i] = req.Name ?? string.Empty;
                targets[i] = req.TargetNode;
            }

            try
            {
                ArrayOf<Variant> output = await Session.CallAsync(
                    CategoryId,
                    methodId,
                    ct,
                    Variant.From(names.ToArrayOf()),
                    Variant.From(targets.ToArrayOf()))
                    .ConfigureAwait(false);
                return ExtractStatusCodeArray(output);
            }
            catch (ServiceResultException sre)
            {
                throw AliasNameClientErrors.Translate(
                    sre.StatusCode, "DeleteAliasesFromCategory", CategoryId);
            }
        }

        /// <summary>
        /// Reads the category's <c>LastChange</c> property (Part 17
        /// §6.3.1) — a <c>VersionTime</c> (<c>uint</c>) that advances
        /// monotonically on every Add/Delete batch. Returns <c>null</c>
        /// when the category does not expose <c>LastChange</c>.
        /// </summary>
        public async Task<uint?> ReadLastChangeAsync(CancellationToken ct = default)
        {
            NodeId lastChangeId;
            try
            {
                lastChangeId = await ResolveMethodIdAsync(
                    BrowseNames.LastChange, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotFound)
            {
                return null;
            }

            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = lastChangeId,
                    AttributeId = Attributes.Value
                }
            ];

            ReadResponse response = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                nodesToRead,
                ct).ConfigureAwait(false);

            if (response.Results.Count == 0
                || StatusCode.IsBad(response.Results[0].StatusCode))
            {
                return null;
            }
            if (response.Results[0].WrappedValue.TryGetValue(out uint value))
            {
                return value;
            }
            return null;
        }

        /// <summary>
        /// Asynchronously enumerates the sub-categories of this category
        /// (Part 17 §6.3.1 <c>SubAliasNameCategories</c>) via a forward
        /// browse on <see cref="ReferenceTypeIds.Organizes"/>.
        /// </summary>
        public async IAsyncEnumerable<AliasNameSubCategoryInfo>
            EnumerateSubCategoriesAsync(
                [EnumeratorCancellation] CancellationToken ct = default)
        {
            var description = new BrowseDescription
            {
                NodeId = CategoryId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                IncludeSubtypes = true,
                NodeClassMask = (uint)NodeClass.Object,
                ResultMask = (uint)(BrowseResultMask.BrowseName
                    | BrowseResultMask.DisplayName
                    | BrowseResultMask.TypeDefinition)
            };
            ArrayOf<BrowseDescription> requests = [description];
            BrowseResponse response = await Session.BrowseAsync(
                null,
                null,
                0,
                requests,
                ct).ConfigureAwait(false);

            if (response.Results.Count == 0)
            {
                yield break;
            }

            BrowseResult br = response.Results[0];
            if (StatusCode.IsBad(br.StatusCode))
            {
                throw new ServiceResultException(br.StatusCode);
            }

            int refCount = br.References.Count;
            var refs = new ReferenceDescription[refCount];
            for (int i = 0; i < refCount; i++)
            {
                refs[i] = br.References[i];
            }
            foreach (ReferenceDescription r in refs)
            {
                if (!r.TypeDefinition.Equals(ObjectTypeIds.AliasNameCategoryType))
                {
                    continue;
                }
                NodeId localId = ExpandedNodeId.ToNodeId(
                    r.NodeId, Session.NamespaceUris);
                if (localId.IsNull)
                {
                    continue;
                }
                yield return new AliasNameSubCategoryInfo(
                    localId,
                    r.BrowseName,
                    r.DisplayName);
            }
        }

        // --------------------------------------------------------------
        // Internal helpers
        // --------------------------------------------------------------

        /// <summary>
        /// Resolves a child method/property NodeId on this category,
        /// caching the result.
        /// </summary>
        internal async Task<NodeId> ResolveMethodIdAsync(
            string childBrowseName,
            CancellationToken ct)
        {
            if (m_methodIdCache.TryGet(childBrowseName, out NodeId cached))
            {
                if (cached.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotFound,
                        "Category " + CategoryId +
                        " does not expose '" + childBrowseName + "'.");
                }
                return cached;
            }

            var relativePath = new RelativePath
            {
                Elements =
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IsInverse = false,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(childBrowseName)
                    }
                ]
            };
            var browsePath = new BrowsePath
            {
                StartingNode = CategoryId,
                RelativePath = relativePath
            };
            ArrayOf<BrowsePath> requests = [browsePath];

            TranslateBrowsePathsToNodeIdsResponse response =
                await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, requests, ct).ConfigureAwait(false);

            if (response.Results.Count == 0
                || StatusCode.IsBad(response.Results[0].StatusCode)
                || response.Results[0].Targets.Count == 0)
            {
                m_methodIdCache.Set(childBrowseName, NodeId.Null);
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "Category " + CategoryId +
                    " does not expose '" + childBrowseName + "'.");
            }
            ExpandedNodeId target = response.Results[0].Targets[0].TargetId;
            NodeId local = ExpandedNodeId.ToNodeId(target, Session.NamespaceUris);
            if (local.IsNull)
            {
                m_methodIdCache.Set(childBrowseName, NodeId.Null);
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "Category " + CategoryId +
                    " does not expose '" + childBrowseName + "'.");
            }
            m_methodIdCache.Set(childBrowseName, local);
            return local;
        }

        private static IReadOnlyList<AliasNameDataType> ExtractAliasNameDataTypeList(
            ArrayOf<Variant> output)
        {
            var result = new List<AliasNameDataType>();
            if (output.Count == 0)
            {
                return result;
            }
            if (!output[0].TryGetStructure(out ArrayOf<AliasNameDataType> arr))
            {
                return result;
            }
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] != null)
                {
                    result.Add(arr[i]);
                }
            }
            return result;
        }

        private static IReadOnlyList<AliasNameVerboseDataType> ExtractAliasNameVerboseDataTypeList(
            ArrayOf<Variant> output)
        {
            var result = new List<AliasNameVerboseDataType>();
            if (output.Count == 0)
            {
                return result;
            }
            if (!output[0].TryGetStructure(out ArrayOf<AliasNameVerboseDataType> arr))
            {
                return result;
            }
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] != null)
                {
                    result.Add(arr[i]);
                }
            }
            return result;
        }

        private static StatusCode[] ExtractStatusCodeArray(ArrayOf<Variant> output)
        {
            if (output.Count == 0)
            {
                return [];
            }
            if (!output[0].TryGetValue(out ArrayOf<StatusCode> codes))
            {
                return [];
            }
            var result = new StatusCode[codes.Count];
            for (int i = 0; i < codes.Count; i++)
            {
                result[i] = codes[i];
            }
            return result;
        }

        private readonly MethodIdCache m_methodIdCache;

        private sealed class MethodIdCache
        {
            public bool TryGet(string browseName, out NodeId nodeId)
            {
                lock (m_lock)
                {
                    return m_lookup.TryGetValue(browseName, out nodeId);
                }
            }

            public void Set(string browseName, NodeId nodeId)
            {
                lock (m_lock)
                {
                    m_lookup[browseName] = nodeId;
                }
            }

            public void SeedKnown(string browseName, NodeId nodeId)
            {
                m_lookup[browseName] = nodeId;
            }

            private readonly Dictionary<string, NodeId> m_lookup = [];
            private readonly object m_lock = new();
        }

        private static class StandardMethodIdCache
        {
            public static MethodIdCache For(NodeId categoryId)
            {
                var cache = new MethodIdCache();
                if (categoryId.Equals(ObjectIds.Aliases))
                {
                    cache.SeedKnown(BrowseNames.FindAlias,
                        MethodIds.Aliases_FindAlias);
                    cache.SeedKnown(BrowseNames.LastChange,
                        VariableIds.Aliases_LastChange);
                }
                else if (categoryId.Equals(ObjectIds.TagVariables))
                {
                    cache.SeedKnown(BrowseNames.FindAlias,
                        MethodIds.TagVariables_FindAlias);
                }
                else if (categoryId.Equals(ObjectIds.Topics))
                {
                    cache.SeedKnown(BrowseNames.FindAlias,
                        MethodIds.Topics_FindAlias);
                }
                return cache;
            }
        }
    }
}
