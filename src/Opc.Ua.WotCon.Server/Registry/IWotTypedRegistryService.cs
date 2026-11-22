// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// Additive exact-authority provisioning capability. Allocation and structural creation
    /// commit together through the registry's existing store.
    /// </summary>
    public interface IWotTypedRegistryService
    {
        /// <summary>
        /// Creates a group for an exact kind/catalogue authority, rejecting an existing group.
        /// </summary>
        /// <param name="kind">The wo t document kind enum.</param>
        /// <param name="catalogUri">The catalog URI.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<WotDocumentGroupResult> CreateDocumentGroupAsync(
            WoTDocumentKindEnum kind,
            string catalogUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically resolves or creates a group for an exact kind/catalogue authority.
        /// </summary>
        /// <param name="kind">The wo t document kind enum.</param>
        /// <param name="catalogUri">The catalog URI.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<WotDocumentGroupResult> GetOrCreateDocumentGroupAsync(
            WoTDocumentKindEnum kind,
            string catalogUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an exact Version under the group's exact document authority.
        /// Empty VersionId requests the next allocation, not a pending or default Version.
        /// </summary>
        /// <param name="groupId">The group ID.</param>
        /// <param name="kind">The wo t document kind enum.</param>
        /// <param name="sourceId">The source ID.</param>
        /// <param name="versionId">The version ID.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<WotDocumentResourceResult> CreateDocumentResourceAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string sourceId,
            string versionId = "",
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically resolves or creates a Resource and exact Version. Empty VersionId
        /// selects an existing Resource's default, or allocates its first Version.
        /// </summary>
        /// <param name="groupId">The group ID.</param>
        /// <param name="kind">The wo t document kind enum.</param>
        /// <param name="sourceId">The source ID.</param>
        /// <param name="versionId">The version ID.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<WotDocumentResourceResult> GetOrCreateDocumentResourceAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string sourceId,
            string versionId = "",
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The committed group allocation and whether this invocation created it.
    /// </summary>
    /// <param name="Group">The complete immutable group, including its exact CatalogUri.</param>
    /// <param name="Created">Whether this invocation committed a new group.</param>
    public sealed record WotDocumentGroupResult(WotResourceGroup Group, bool Created);

    /// <summary>
    /// The committed logical Resource and exact Version allocation.
    /// </summary>
    /// <param name="Resource">The logical Resource with immutable source identity.</param>
    /// <param name="Version">The exact Version selected or created.</param>
    /// <param name="CreatedResource">Whether this invocation created the logical Resource.</param>
    /// <param name="CreatedVersion">Whether this invocation created the exact Version.</param>
    public sealed record WotDocumentResourceResult(
        WotResource Resource,
        WotResourceVersion Version,
        bool CreatedResource,
        bool CreatedVersion);
}
