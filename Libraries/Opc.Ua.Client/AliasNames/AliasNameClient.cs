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
    /// Built on top of the source-generated
    /// <see cref="AliasNameCategoryTypeClient"/> ObjectType proxy that
    /// ships with the standard NodeSet. The proxy handles
    /// <c>CallRequest</c> packing/unpacking and per-call validation
    /// (<c>BadUnexpectedError</c> on malformed output) — this wrapper
    /// adds the ergonomic surface: typed exceptions for service-level
    /// status codes, sub-category browse, and an
    /// <see cref="ReadLastChangeAsync"/> helper for the
    /// <c>VersionTime</c> property (which the proxy does not cover).
    /// </para>
    /// <para>
    /// Service-level failures map to typed .NET exceptions:
    /// <c>BadUserAccessDenied</c> → <see cref="UnauthorizedAccessException"/>,
    /// <c>BadNotSupported</c>/<c>BadNotImplemented</c> →
    /// <see cref="NotSupportedException"/>; everything else propagates as
    /// <see cref="ServiceResultException"/>.
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
            Proxy = new AliasNameCategoryTypeClient(
                session,
                categoryId,
                session.MessageContext.Telemetry);
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
        /// The underlying source-generated proxy. Exposed for advanced
        /// scenarios that need direct access to the raw proxy methods.
        /// </summary>
        public AliasNameCategoryTypeClient Proxy { get; }

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
            try
            {
                ArrayOf<AliasNameDataType> result = await Proxy
                    .FindAliasAsync(
                        aliasNameSearchPattern ?? string.Empty,
                        referenceTypeFilter ?? NodeId.Null,
                        ct)
                    .ConfigureAwait(false);
                return ToList(result);
            }
            catch (ServiceResultException sre)
            {
                throw AliasNameClientErrors.Translate(
                    sre.StatusCode, "FindAlias", CategoryId);
            }
        }

        /// <summary>
        /// Calls <c>FindAliasVerbose</c> (Part 17 §6.3.3). The proxy issues
        /// the call against <c>MethodIds.AliasNameCategoryType_FindAliasVerbose</c>
        /// — when the category does not expose the optional method the
        /// server replies with <c>BadNotImplemented</c>/<c>BadMethodInvalid</c>
        /// which is translated into <see cref="NotSupportedException"/>.
        /// </summary>
        public async Task<IReadOnlyList<AliasNameVerboseDataType>> FindAliasVerboseAsync(
            string aliasNameSearchPattern,
            NodeId? referenceTypeFilter = null,
            CancellationToken ct = default)
        {
            try
            {
                ArrayOf<AliasNameVerboseDataType> result = await Proxy
                    .FindAliasVerboseAsync(
                        aliasNameSearchPattern ?? string.Empty,
                        referenceTypeFilter ?? NodeId.Null,
                        ct)
                    .ConfigureAwait(false);
                return ToList(result);
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
                ArrayOf<StatusCode> codes = await Proxy
                    .AddAliasesToCategoryAsync(
                        names.ToArrayOf(),
                        targets.ToArrayOf(),
                        servers.ToArrayOf(),
                        refType,
                        ct)
                    .ConfigureAwait(false);
                return ToArray(codes);
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
                ArrayOf<StatusCode> codes = await Proxy
                    .DeleteAliasesFromCategoryAsync(
                        names.ToArrayOf(),
                        targets.ToArrayOf(),
                        ct)
                    .ConfigureAwait(false);
                return ToArray(codes);
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
            NodeId lastChangeId = ResolveStandardLastChange(CategoryId);
            if (lastChangeId.IsNull)
            {
                // Fall back to a browse-path lookup for non-standard
                // categories.
                lastChangeId = await ResolveChildAsync(
                    BrowseNames.LastChange, ct).ConfigureAwait(false);
                if (lastChangeId.IsNull)
                {
                    return null;
                }
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

        private async Task<NodeId> ResolveChildAsync(
            string childBrowseName,
            CancellationToken ct)
        {
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
                return NodeId.Null;
            }
            ExpandedNodeId target = response.Results[0].Targets[0].TargetId;
            return ExpandedNodeId.ToNodeId(target, Session.NamespaceUris);
        }

        private static NodeId ResolveStandardLastChange(NodeId categoryId)
        {
            // Only the standard Aliases (i=23470) object instantiates
            // LastChange in the shipped NodeSet (Part 17 §9.2).
            if (categoryId.Equals(ObjectIds.Aliases))
            {
                return VariableIds.Aliases_LastChange;
            }
            return NodeId.Null;
        }

        private static List<T> ToList<T>(ArrayOf<T> arr)
        {
            var result = new List<T>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] != null)
                {
                    result.Add(arr[i]);
                }
            }
            return result;
        }

        private static StatusCode[] ToArray(ArrayOf<StatusCode> arr)
        {
            var result = new StatusCode[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                result[i] = arr[i];
            }
            return result;
        }
    }
}
