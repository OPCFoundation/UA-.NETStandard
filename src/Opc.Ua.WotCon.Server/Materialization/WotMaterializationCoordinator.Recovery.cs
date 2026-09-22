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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    public sealed partial class WotMaterializationCoordinator
    {
        /// <summary>
        /// Restores the committed runtime image without selecting pending Versions or deciding another generation.
        /// Returns false when the registry has no committed materialization to restore.
        /// </summary>
        public async ValueTask<bool> RecoverAsync(CancellationToken cancellationToken = default)
        {
            if (!TryBeginOperation(allowDisposed: false))
            {
                throw new ObjectDisposedException(nameof(WotMaterializationCoordinator));
            }
            try
            {
                await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    bool recovered = await RecoverCommittedImageAsync(cancellationToken).ConfigureAwait(false);
                    m_publicationRecoveryRequired = false;
                    return recovered;
                }
                finally
                {
                    m_mutex.Release();
                }
            }
            finally
            {
                EndOperation();
            }
        }

        private async ValueTask<bool> RecoverCommittedImageAsync(CancellationToken cancellationToken)
        {
            if (m_registry is not IWotRegistryRecoveryResolver resolver)
            {
                if (m_registry.Current.RefreshGeneration == 0)
                {
                    return false;
                }
                throw new NotSupportedException("The registry owner cannot resolve authoritative recovery state.");
            }
            m_recoveryMetadataPending |= await resolver.ResolveRecoveryAsync(cancellationToken).ConfigureAwait(false);
            while (true)
            {
                WotRegistrySnapshot snapshot = m_registry.Current;
                if (snapshot.RefreshGeneration == 0)
                {
                    if (m_recoveryMetadataPending)
                    {
                        if (CommittedPublication.RefreshGeneration != 0)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadInvalidState, "The deciding record precedes the known live publication.");
                        }
                        if (!await ReconcileResolvedMetadataAsync(snapshot, cancellationToken).ConfigureAwait(false))
                        {
                            continue;
                        }
                        m_recoveryMetadataPending = false;
                    }
                    return false;
                }
                if (RuntimeMatches(snapshot))
                {
                    if (m_recoveryMetadataPending)
                    {
                        if (!await ReconcileResolvedMetadataAsync(snapshot, cancellationToken).ConfigureAwait(false))
                        {
                            continue;
                        }
                        m_recoveryMetadataPending = false;
                    }
                    return true;
                }
                if (m_sourceHost is not IWotInvocationProjectionHost host ||
                    m_registry is not IWotInvocationRegistryPublicationService
                        { SupportsPreparedPublication: true })
                {
                    throw new NotSupportedException("The configured owners cannot recover an authoritative publication.");
                }
                WotCanonicalViewState? graph = snapshot.CanonicalViewGraphState.IsNull ||
                    snapshot.CanonicalViewGraphState.Length == 0
                    ? null : WotCanonicalViewState.Parse(snapshot.CanonicalViewGraphState);
                if (graph is null && !CommittedPublication.Views.IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The deciding record has lost the current canonical graph history.");
                }
                var recordedViews = graph?.Views.ToList()
                    .ToDictionary(view => view.ResourceXid, StringComparer.Ordinal)
                    ?? new Dictionary<string, WotCanonicalViewPublication>(StringComparer.Ordinal);
                var selected = new List<WoTResourceSelectorDataType>();
                foreach (WotResource resource in snapshot.AllResources())
                {
                    if (resource.ActiveVersionId is null)
                    {
                        continue;
                    }
                    if (resource.CommittedVersion is null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            "An active publication has no exact committed input record; recovery cannot infer its bytes.");
                    }
                    recordedViews.TryGetValue(resource.Xid, out WotCanonicalViewPublication? view);
                    if (WotProjectionAdmission.UsesProjectionFormat(
                        resource.CommittedVersion.Format, resource.CommittedVersion.ContentType) &&
                        view is not { Active: true })
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState,
                            "A committed projection Resource is missing from the active canonical publication.");
                    }
                    if (view is { Requested: false })
                    {
                        continue;
                    }
                    selected.Add(new WoTResourceSelectorDataType
                    {
                        Kind = resource.Kind,
                        GroupId = resource.GroupId,
                        ResourceId = resource.ResourceId,
                        VersionId = resource.ActiveVersionId
                    });
                }
                var request = new WotRefreshRequest
                {
                    RequestId = "recovery",
                    Selection = selected.ToImmutableArray(),
                    Options = new WoTRefreshOptionsDataType { Force = true, Atomicity = WoTAtomicityEnum.PerRegistry }
                };
                IWotProjectionPublicationCapture sourceCapture = host.CapturePublication();
                using WotRefreshCapture? inputs = selected.Count == 0 ? null : await CaptureInputsAsync(
                    WotCapturedRefreshRequest.Capture(request), cancellationToken, snapshot.RefreshGeneration,
                    committedInputs: true)
                    .ConfigureAwait(false);
                IWotRegistryPublication registryPublication = await resolver.BeginRecoveryPublicationAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var registryLifetime = registryPublication.ConfigureAwait(false);
                if (registryPublication is not IWotRegistryRecoveryPublication recoveryRegistry)
                {
                    throw new NotSupportedException("The registry invocation cannot recover a committed image.");
                }
                IWotProjectionPublication publication = await sourceCapture.BeginAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var publicationLifetime = publication.ConfigureAwait(false);
                if (!ReferenceEquals(snapshot, registryPublication.Current) || !publication.IsCurrent)
                {
                    continue;
                }
                var capture = new PublicationCapture(m_closures, m_projectionNamespaceUris)
                {
                    RecoveryGeneration = snapshot.RefreshGeneration
                };
                WotRefreshResult? staged = null;
                if (inputs is not null)
                {
                    m_preparing = capture;
                    try
                    {
                        staged = await RefreshCoreAsync(inputs, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        m_preparing = null;
                    }
                    if (staged.Summary.Failed != 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadConfigurationError,
                            "The committed publication cannot be reconstructed: " +
                            string.Join("; ", staged.Results.Where(row => row.Outcome == WoTOutcomeEnum.Failed)
                                .Select(row => row.Message)));
                    }
                }
                else
                {
                    foreach (ClosureState closure in capture.Closures.Values)
                    {
                        if (closure.Handle is not null)
                        {
                            capture.Changes.Add(new CapturedProjection(
                                WotProjectionChange.Remove(closure.Handle), null));
                        }
                        foreach (WotBindingPlan plan in closure.BindingPlans)
                        {
                            capture.Bindings.Add(new BindingAction(plan, false));
                        }
                    }
                    capture.Closures.Clear();
                    capture.Namespaces.Clear();
                }
                if (capture.ViewUpdates.Any(update =>
                    !recordedViews.TryGetValue(update.ResourceXid, out WotCanonicalViewPublication? view) || !view.Active))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "Committed projection Resources require their complete active canonical graph entries.");
                }
                IWotPreparedViewPublication? views = null;
                try
                {
                    if (graph is not null)
                    {
                        if (m_sourceViewHost is not IWotRecoverableViewProjectionHost viewHost)
                        {
                            throw new NotSupportedException("The View host cannot restore committed graph history.");
                        }
                        views = await viewHost.PrepareRecoveryAsync(
                            new WotCommittedPublicationState(snapshot, CommittedPublication.Views),
                            cancellationToken).ConfigureAwait(false);
                    }
                    if (capture.Changes.Count == 0 && views is null)
                    {
                        IWotPreparedRegistryRecovery empty = await recoveryRegistry.PrepareRecoveryAsync(
                            snapshot, snapshot, cancellationToken).ConfigureAwait(false);
                        await using (empty.ConfigureAwait(false))
                        {
                            await empty.ValidateAsync(cancellationToken).ConfigureAwait(false);
                            m_generation = snapshot.RefreshGeneration;
                            m_runtimeInitialized = true;
                            Volatile.Write(ref m_committedPublication, new WotCommittedPublicationState(snapshot));
                            m_recoveryMetadataPending = true;
                            empty.Publish();
                            await CompleteRecoveredMetadataAsync(empty).ConfigureAwait(false);
                        }
                        return true;
                    }
                    IWotPreparedProjectionPublication prepared = await publication.PrepareAsync(
                        capture.Changes.Select(change => change.Change).ToArrayOf(), views, cancellationToken)
                        .ConfigureAwait(false);
                    await using var preparedLifetime = prepared.ConfigureAwait(false);
                    int index = 0;
                    foreach (CapturedProjection change in capture.Changes)
                    {
                        if (change.Candidate is null)
                        {
                            continue;
                        }
                        WotProjectionHandle bound = prepared.Projections[index++];
                        foreach (ClosureState closure in capture.Closures.Values)
                        {
                            if (ReferenceEquals(closure.Handle, change.Candidate))
                            {
                                closure.Handle = bound;
                            }
                        }
                    }
                    if (staged is not null)
                    {
                        BindPreparedSourceRoots(capture, staged, snapshot);
                    }
                    ArrayOf<WotViewProjectionHandle> recoveredViews = prepared.ViewGraph?.Views ?? [];
                    if (prepared.ViewGraph is { } preparedGraph &&
                        preparedGraph.CanonicalViewGraphState != snapshot.CanonicalViewGraphState)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "Recovery changed the decided canonical graph.");
                    }
                    ValidateRecoveredViewMetadata(snapshot, graph, recoveredViews);
                    WotRegistrySnapshot runtime = RebaseRecoveredRoots(snapshot, capture, recoveredViews);
                    foreach (ClosureState closure in capture.Closures.Values)
                    {
                        closure.Generation = snapshot.RefreshGeneration;
                        closure.ViewHandles = [.. recoveredViews.ToList()
                            .Where(view => closure.MemberXids.Contains(view.ResourceXid))];
                        closure.PublishedMetadata = closure.Members.Select(member =>
                        {
                            WotResource resource = runtime.FindResourceByXid(member.Xid)
                                ?? throw new ServiceResultException(
                                    StatusCodes.BadInvalidState, "A recovered owner has no committed Resource.");
                            return new WotResourceProjection(
                                resource.GroupId, resource.ResourceId, resource.LoadState, resource.ActiveVersionId,
                                resource.RefreshGeneration, resource.MaterializedNodeCount, resource.RootNodeId,
                                resource.Validation, resource.Diagnostics, resource.LastRefreshTime);
                        }).ToImmutableArray();
                    }
                    IWotPreparedRegistryRecovery metadata = await recoveryRegistry.PrepareRecoveryAsync(
                        snapshot, runtime, cancellationToken).ConfigureAwait(false);
                    await using var metadataLifetime = metadata.ConfigureAwait(false);
                    var restored = new WotCommittedPublicationState(runtime, recoveredViews,
                        capture.Closures.Values.SelectMany(closure => closure.BindingPlans).ToArrayOf());
                    await prepared.CommitAsync(metadata.ValidateAsync, () =>
                    {
                        m_closures.Clear();
                        foreach (KeyValuePair<string, ClosureState> entry in capture.Closures)
                        {
                            m_closures.Add(entry.Key, entry.Value);
                        }
                        m_projectionNamespaceUris.Clear();
                        m_projectionNamespaceUris.UnionWith(capture.Namespaces);
                        m_generation = snapshot.RefreshGeneration;
                        m_runtimeInitialized = true;
                        Volatile.Write(ref m_committedPublication, restored);
                        try
                        {
                            views?.OnPublished(restored);
                        }
                        finally
                        {
                            m_recoveryMetadataPending = true;
                            metadata.Publish();
                        }
                    }, cancellationToken).ConfigureAwait(false);
                    foreach (BindingAction action in capture.Bindings)
                    {
                        if (action.Activate)
                        {
                            await m_sourceBinders.ActivateAsync(action.Plan, CancellationToken.None).ConfigureAwait(false);
                        }
                        else
                        {
                            await m_sourceBinders.DeactivateAsync(action.Plan, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                    }
                    await CompleteRecoveredMetadataAsync(metadata).ConfigureAwait(false);
                    if (prepared.CleanupFailure is { } failure)
                    {
                        throw new WotRegistryCommitDurabilityUncertainException(runtime, failure);
                    }
                    return true;
                }
                finally
                {
                    if (views is not null)
                    {
                        await views.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        private async ValueTask<bool> ReconcileResolvedMetadataAsync(
            WotRegistrySnapshot snapshot, CancellationToken cancellationToken)
        {
            if (m_sourceHost is not IWotInvocationProjectionHost host ||
                m_registry is not IWotRegistryRecoveryResolver resolver)
            {
                throw new NotSupportedException("The configured owners cannot reconcile a recovered publication.");
            }
            IWotProjectionPublicationCapture capture = host.CapturePublication();
            IWotRegistryPublication registryPublication = await resolver.BeginRecoveryPublicationAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var registryLifetime = registryPublication.ConfigureAwait(false);
            if (registryPublication is not IWotRegistryRecoveryPublication recovery)
            {
                throw new NotSupportedException("The registry invocation cannot reconcile recovered metadata.");
            }
            IWotProjectionPublication source = await capture.BeginAsync(cancellationToken).ConfigureAwait(false);
            await using var sourceLifetime = source.ConfigureAwait(false);
            if (!ReferenceEquals(snapshot, registryPublication.Current) || !source.IsCurrent)
            {
                return false;
            }
            WotCommittedPublicationState previous = CommittedPublication;
            WotCanonicalViewState? graph = snapshot.CanonicalViewGraphState.IsNull ||
                snapshot.CanonicalViewGraphState.Length == 0
                ? null : WotCanonicalViewState.Parse(snapshot.CanonicalViewGraphState);
            ValidateRecoveredViewMetadata(snapshot, graph, previous.Views);
            var roots = previous.RegistrySnapshot.AllResources()
                .Where(resource => resource.ActiveVersionId is not null && !resource.RootNodeId.IsNull)
                .ToDictionary(resource => resource.Xid, resource => resource.RootNodeId, StringComparer.Ordinal);
            WotRegistrySnapshot runtime = RebaseRecoveredRootMap(snapshot, roots);
            IWotPreparedRegistryRecovery metadata = await recovery.PrepareRecoveryAsync(
                snapshot, runtime, cancellationToken).ConfigureAwait(false);
            await using (metadata.ConfigureAwait(false))
            {
                await metadata.ValidateAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_committedPublication,
                    new WotCommittedPublicationState(runtime, previous.Views, previous.ActiveBindingPlans));
                m_recoveryMetadataPending = true;
                metadata.Publish();
                await CompleteRecoveredMetadataAsync(metadata).ConfigureAwait(false);
            }
            return true;
        }

        private async ValueTask CompleteRecoveredMetadataAsync(IWotPreparedRegistryRecovery metadata)
        {
            await metadata.CompleteAsync().ConfigureAwait(false);
            m_recoveryMetadataPending = false;
        }

        private bool RuntimeMatches(WotRegistrySnapshot snapshot)
        {
            WotRegistrySnapshot previous = CommittedPublication.RegistrySnapshot;
            return m_runtimeInitialized && previous.RefreshGeneration == snapshot.RefreshGeneration &&
                previous.CanonicalViewGraphState == snapshot.CanonicalViewGraphState &&
                previous.AllResources().Where(resource => resource.ActiveVersionId is not null)
                    .Select(resource => (resource.Xid, resource.ActiveVersionId,
                        resource.ActiveVersion?.Epoch ?? 0, resource.ActiveVersion?.DigestHex ?? string.Empty))
                    .OrderBy(value => value.Xid, StringComparer.Ordinal)
                    .SequenceEqual(snapshot.AllResources().Where(resource => resource.ActiveVersionId is not null)
                        .Select(resource => (resource.Xid, resource.ActiveVersionId,
                            resource.ActiveVersion?.Epoch ?? 0, resource.ActiveVersion?.DigestHex ?? string.Empty))
                        .OrderBy(value => value.Xid, StringComparer.Ordinal));
        }

        private void ValidateRecoveredViewMetadata(
            WotRegistrySnapshot snapshot,
            WotCanonicalViewState? graph,
            ArrayOf<WotViewProjectionHandle> views)
        {
            var active = graph?.Views.ToList().Where(view => view.Active)
                .ToDictionary(view => view.ResourceXid, StringComparer.Ordinal)
                ?? new Dictionary<string, WotCanonicalViewPublication>(StringComparer.Ordinal);
            if (active.Count != views.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "Recovery has an incomplete canonical View image.");
            }
            var bound = new HashSet<string>(StringComparer.Ordinal);
            foreach (WotViewProjectionHandle view in views)
            {
                WotResource? resource = snapshot.FindResourceByXid(view.ResourceXid);
                if (!bound.Add(view.ResourceXid) || !active.TryGetValue(view.ResourceXid, out var recorded) ||
                    resource is null || resource.ActiveVersionId is null || resource.CommittedVersion is null ||
                    resource.MaterializedNodeCount != recorded.MaterializedNodeCount ||
                    view.MaterializedNodeCount != recorded.MaterializedNodeCount ||
                    resource.RootNodeId.IsNull || view.ViewNodeId.IsNull ||
                    resource.RootNodeId.WithNamespaceIndex(0) != view.ViewNodeId.WithNamespaceIndex(0) ||
                    ResolveRootNodeId(recorded.ViewNodeId) != view.ViewNodeId)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Committed Resource metadata disagrees with its canonical View.");
                }
            }
        }

        private static WotRegistrySnapshot RebaseRecoveredRoots(
            WotRegistrySnapshot snapshot, PublicationCapture capture, ArrayOf<WotViewProjectionHandle> views)
        {
            var roots = new Dictionary<string, NodeId>(StringComparer.Ordinal);
            foreach (WotResourceProjection projection in capture.Projections)
            {
                WotResource? resource = snapshot.FindResource(projection.GroupId, projection.ResourceId);
                if (resource is not null && !projection.RootNodeId.IsNull)
                {
                    roots[resource.Xid] = projection.RootNodeId;
                }
            }
            foreach (WotViewProjectionHandle view in views)
            {
                roots[view.ResourceXid] = view.ViewNodeId;
            }
            return RebaseRecoveredRootMap(snapshot, roots);
        }

        private static WotRegistrySnapshot RebaseRecoveredRootMap(
            WotRegistrySnapshot snapshot, Dictionary<string, NodeId> roots)
        {
            ImmutableDictionary<string, WotResourceGroup> groups = snapshot.Groups;
            foreach (WotResourceGroup group in snapshot.Groups.Values)
            {
                ImmutableDictionary<string, WotResource> resources = group.Resources;
                foreach (WotResource resource in group.Resources.Values)
                {
                    if (roots.TryGetValue(resource.Xid, out NodeId root))
                    {
                        resources = resources.SetItem(resource.ResourceId, resource.With(rootNodeId: root));
                    }
                }
                groups = groups.SetItem(group.GroupId, group.WithResources(resources, group.Epoch));
            }
            return new WotRegistrySnapshot(snapshot.Generation, groups, snapshot.Labels,
                snapshot.CanonicalViewGraphState, snapshot.RefreshGeneration);
        }

        private bool m_runtimeInitialized;
        private bool m_recoveryMetadataPending;
        private bool m_publicationRecoveryRequired;
    }
}
