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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// A request to create or update a document resource (upload a TD/TM
    /// version). The registry validates and stores the version and, when
    /// <see cref="SetAsDefault"/> is set, points the resource default/desired
    /// version at the new version.
    /// </summary>
    public sealed class WotUpsertResourceRequest
    {
        /// <summary>Gets or sets the target group id (defaults per <see cref="Kind"/>).</summary>
        public string? GroupId { get; set; }

        /// <summary>Gets or sets the resource id; derived from the document when omitted.</summary>
        public string? ResourceId { get; set; }

        /// <summary>Gets or sets the document kind.</summary>
        public WoTDocumentKindEnum Kind { get; set; } = WoTDocumentKindEnum.ThingDescription;

        /// <summary>Gets or sets the raw document source bytes.</summary>
        public ReadOnlyMemory<byte> Content { get; set; }

        /// <summary>Gets or sets the document media type.</summary>
        public string ContentType { get; set; } = "application/td+json";

        /// <summary>Gets or sets the document format tag.</summary>
        public string Format { get; set; } = "WoT-TD/1.1";

        /// <summary>Gets or sets an optional resource display name.</summary>
        public string? Name { get; set; }

        /// <summary>Gets or sets an optional resource description.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets whether the new version becomes the resource's default
        /// and desired version. Defaults to <c>true</c>.
        /// </summary>
        public bool SetAsDefault { get; set; } = true;
    }

    /// <summary>
    /// The result of a registry mutation.
    /// </summary>
    public sealed class WotRegistryMutationResult
    {
        internal WotRegistryMutationResult(
            WoTOutcomeEnum outcome,
            WotResource? resource,
            long generation,
            ImmutableArray<string> diagnostics,
            string? message = null)
        {
            Outcome = outcome;
            Resource = resource;
            Generation = generation;
            Diagnostics = diagnostics.IsDefault ? ImmutableArray<string>.Empty : diagnostics;
            Message = message ?? string.Empty;
        }

        /// <summary>Gets the outcome of the mutation.</summary>
        public WoTOutcomeEnum Outcome { get; }

        /// <summary>Gets the affected resource snapshot, if any.</summary>
        public WotResource? Resource { get; }

        /// <summary>Gets the registry generation after the mutation.</summary>
        public long Generation { get; }

        /// <summary>Gets diagnostics produced by the mutation.</summary>
        public ImmutableArray<string> Diagnostics { get; }

        /// <summary>Gets a human-readable message.</summary>
        public string Message { get; }

        /// <summary>Gets whether the mutation changed the registry contents.</summary>
        public bool Changed => Outcome is WoTOutcomeEnum.Success or WoTOutcomeEnum.Warning;
    }

    /// <summary>
    /// Describes a change to the registry snapshot. Content mutations
    /// (<see cref="ProjectionOnly"/> is <c>false</c>) drive re-materialization;
    /// projection callbacks (<see cref="ProjectionOnly"/> is <c>true</c>) only
    /// refresh browseable projection state and must not re-trigger the
    /// materialization coordinator.
    /// </summary>
    public sealed class WotRegistryChangedEventArgs : EventArgs
    {
        internal WotRegistryChangedEventArgs(
            WotRegistrySnapshot previous,
            WotRegistrySnapshot current,
            IReadOnlyList<string> changedResourceXids,
            bool projectionOnly)
        {
            Previous = previous;
            Current = current;
            ChangedResourceXids = changedResourceXids;
            ProjectionOnly = projectionOnly;
        }

        /// <summary>Gets the snapshot before the change.</summary>
        public WotRegistrySnapshot Previous { get; }

        /// <summary>Gets the snapshot after the change.</summary>
        public WotRegistrySnapshot Current { get; }

        /// <summary>Gets the xids of the resources that changed.</summary>
        public IReadOnlyList<string> ChangedResourceXids { get; }

        /// <summary>
        /// Gets whether the change only recorded projection state (and must not
        /// re-trigger materialization).
        /// </summary>
        public bool ProjectionOnly { get; }
    }

    /// <summary>
    /// The projection state recorded back into the registry snapshot by the
    /// materialization coordinator after a refresh.
    /// </summary>
    public sealed class WotResourceProjection
    {
        /// <summary>Initializes a new projection record.</summary>
        public WotResourceProjection(
            string groupId,
            string resourceId,
            WoTLoadStateEnum loadState,
            string? activeVersionId,
            uint refreshGeneration,
            int materializedNodeCount,
            NodeId? rootNodeId,
            WoTValidationOutcomeDataType? validation,
            ImmutableArray<string> diagnostics,
            DateTime lastRefreshTime)
        {
            GroupId = groupId;
            ResourceId = resourceId;
            LoadState = loadState;
            ActiveVersionId = activeVersionId;
            RefreshGeneration = refreshGeneration;
            MaterializedNodeCount = materializedNodeCount;
            RootNodeId = rootNodeId;
            Validation = validation;
            Diagnostics = diagnostics.IsDefault ? ImmutableArray<string>.Empty : diagnostics;
            LastRefreshTime = lastRefreshTime;
        }

        /// <summary>Gets the group id.</summary>
        public string GroupId { get; }

        /// <summary>Gets the resource id.</summary>
        public string ResourceId { get; }

        /// <summary>Gets the resulting load state.</summary>
        public WoTLoadStateEnum LoadState { get; }

        /// <summary>Gets the active version id, if any.</summary>
        public string? ActiveVersionId { get; }

        /// <summary>Gets the refresh generation.</summary>
        public uint RefreshGeneration { get; }

        /// <summary>Gets the materialized node count.</summary>
        public int MaterializedNodeCount { get; }

        /// <summary>Gets the root node of the projection, if any.</summary>
        public NodeId? RootNodeId { get; }

        /// <summary>Gets the validation outcome, if any.</summary>
        public WoTValidationOutcomeDataType? Validation { get; }

        /// <summary>Gets the diagnostics.</summary>
        public ImmutableArray<string> Diagnostics { get; }

        /// <summary>Gets the UTC last-refresh time.</summary>
        public DateTime LastRefreshTime { get; }

        /// <summary>Gets whether the projection failed to keep a previous active generation.</summary>
        public bool RetainPreviousActiveVersion { get; init; }
    }

    /// <summary>
    /// The stable, injectable registry service. It owns the current immutable
    /// registry snapshot, serialises mutations, enforces resource bounds,
    /// persists through an <see cref="IWotRegistryStore"/>, and raises change
    /// notifications the materialization coordinator and NodeManager react to.
    /// </summary>
    public interface IWotRegistryService
    {
        /// <summary>Gets the current immutable snapshot.</summary>
        WotRegistrySnapshot Current { get; }

        /// <summary>Gets the configured resource bounds.</summary>
        WotRegistryPersistenceBounds Bounds { get; }

        /// <summary>
        /// Raised after a content mutation (upsert/delete/set-default/set-enabled)
        /// or a projection callback. Consumers filter on
        /// <see cref="WotRegistryChangedEventArgs.ProjectionOnly"/>.
        /// </summary>
        event EventHandler<WotRegistryChangedEventArgs>? Changed;

        /// <summary>Loads persisted state from the backing store.</summary>
        ValueTask InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>Gets, or creates, a group.</summary>
        ValueTask<WotResourceGroup> GetOrCreateGroupAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string? name = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a group, failing (returning <c>null</c>) when a group with the
        /// same id already exists. Use <see cref="GetOrCreateGroupAsync"/> for the
        /// idempotent create-or-get form.
        /// </summary>
        ValueTask<WotResourceGroup?> TryCreateGroupAsync(
            string groupId,
            WoTDocumentKindEnum kind,
            string? name = null,
            CancellationToken cancellationToken = default);

        /// <summary>Deletes a group and every resource it contains.</summary>
        ValueTask<WotRegistryMutationResult> DeleteGroupAsync(
            string groupId,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets, or creates, a content-less placeholder resource. The resource is
        /// projected only once a version has been uploaded (through the inherited
        /// FileType write path or <see cref="UpsertResourceAsync"/>).
        /// </summary>
        ValueTask<(WotResource Resource, bool Created)> GetOrCreateResourceAsync(
            string groupId,
            string resourceId,
            WoTDocumentKindEnum kind,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a content-less placeholder resource, failing (returning
        /// <c>null</c>) when the resource already exists. Use
        /// <see cref="GetOrCreateResourceAsync"/> for the idempotent form.
        /// </summary>
        ValueTask<WotResource?> TryCreateResourceAsync(
            string groupId,
            string resourceId,
            WoTDocumentKindEnum kind,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates the default version of a resource (format, and best-effort
        /// compatibility), records the outcome as projection state, and returns it
        /// without changing the resource's active projection.
        /// </summary>
        ValueTask<WoTValidationOutcomeDataType> ValidateResourceAsync(
            string groupId,
            string resourceId,
            CancellationToken cancellationToken = default);

        /// <summary>Creates or updates a document resource (uploads a version).</summary>
        ValueTask<WotRegistryMutationResult> UpsertResourceAsync(
            WotUpsertResourceRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>Deletes a resource and all of its versions.</summary>
        ValueTask<WotRegistryMutationResult> DeleteResourceAsync(
            string groupId,
            string resourceId,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default);

        /// <summary>Sets the default (and desired) version of a resource.</summary>
        ValueTask<WotRegistryMutationResult> SetDefaultVersionAsync(
            string groupId,
            string resourceId,
            string versionId,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default);

        /// <summary>Enables or disables a resource for projection.</summary>
        ValueTask<WotRegistryMutationResult> SetEnabledAsync(
            string groupId,
            string resourceId,
            bool enabled,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds or updates a registry-level xRegistry label (attribute). The
        /// registry has no separate per-entity epoch, so
        /// <paramref name="expectedEpoch"/> is compared against the current
        /// snapshot <see cref="WotRegistrySnapshot.Generation"/>. Throws a
        /// <see cref="ServiceResultException"/> for an invalid/reserved key,
        /// an over-long value, or when the entity already holds the
        /// configured maximum number of labels.
        /// </summary>
        ValueTask<WotRegistryMutationResult> AddRegistryLabelAsync(
            string key,
            string value,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default);

        /// <summary>Removes a registry-level xRegistry label (attribute).</summary>
        ValueTask<WotRegistryMutationResult> RemoveRegistryLabelAsync(
            string key,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds or updates a group-level xRegistry label (attribute). See
        /// <see cref="AddRegistryLabelAsync"/> for the validation and
        /// concurrency semantics (here compared against the group's own
        /// <see cref="WotResourceGroup.Epoch"/>).
        /// </summary>
        ValueTask<WotRegistryMutationResult> AddGroupLabelAsync(
            string groupId,
            string key,
            string value,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default);

        /// <summary>Removes a group-level xRegistry label (attribute).</summary>
        ValueTask<WotRegistryMutationResult> RemoveGroupLabelAsync(
            string groupId,
            string key,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds or updates a resource-level xRegistry label (attribute). See
        /// <see cref="AddRegistryLabelAsync"/> for the validation and
        /// concurrency semantics (here compared against the resource's own
        /// <see cref="WotResource.Epoch"/>).
        /// </summary>
        ValueTask<WotRegistryMutationResult> AddResourceLabelAsync(
            string groupId,
            string resourceId,
            string key,
            string value,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default);

        /// <summary>Removes a resource-level xRegistry label (attribute).</summary>
        ValueTask<WotRegistryMutationResult> RemoveResourceLabelAsync(
            string groupId,
            string resourceId,
            string key,
            long? expectedEpoch = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records projection state produced by the materialization coordinator
        /// back into the snapshot so the NodeManager can browse load state,
        /// active version, generation and validation outcome. Raises a
        /// projection-only change and never re-triggers materialization.
        /// </summary>
        ValueTask ApplyProjectionResultsAsync(
            IReadOnlyList<WotResourceProjection> projections,
            CancellationToken cancellationToken = default);
    }
}
