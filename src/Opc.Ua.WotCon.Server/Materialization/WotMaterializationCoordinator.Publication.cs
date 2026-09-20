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
        /// Gets the last runtime image published by this coordinator.
        /// </summary>
        public WotCommittedPublicationState CommittedPublication =>
            Volatile.Read(ref m_committedPublication);

        private Dictionary<string, ClosureState> ClosureStates => m_preparing?.Closures ?? m_closures;
        private HashSet<string> ProjectionNamespaces => m_preparing?.Namespaces ?? m_projectionNamespaceUris;

        private async ValueTask<WotRefreshResult> RefreshPreparedRegistryAsync(
            WotRefreshCapture refresh,
            WotRegistrySnapshot snapshot,
            DateTime start,
            CancellationToken cancellationToken)
        {
            if (m_sourceHost is not IWotPreparedProjectionHost { SupportsPreparedPublication: true } host ||
                m_registry is not IWotPreparedRegistryPublicationService { SupportsPreparedPublication: true } registry)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The configured owners cannot prepare a PerRegistry publication.");
            }
            WotCapturedRefreshRequest request = refresh.Request;
            var capture = new PublicationCapture(m_closures, m_projectionNamespaceUris);
            WotRefreshResult staged;
            m_preparing = capture;
            try
            {
                staged = await RefreshCoreAsync(refresh, start, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_preparing = null;
            }

            bool failed = staged.Summary.Failed != 0;
            bool dryRun = request.DryRun;
            bool changed = capture.Changes.Count != 0 ||
                capture.ViewUpdates.Count != 0 || capture.ViewRemovals.Count != 0;
            if (failed || dryRun || !changed)
            {
                WotRefreshResult result = NormalizeUncommitted(staged, snapshot, failed, dryRun);
                foreach (WotMaterializationEventArgs change in capture.Events)
                {
                    if (change.Kind is WotMaterializationEventKind.ValidationFailure or
                        WotMaterializationEventKind.LoadFailure or WotMaterializationEventKind.BindingFailure)
                    {
                        RaiseEvent(CopyEvent(change, m_generation, request.RequestId));
                    }
                }
                RaiseEvent(CreateCompletion(result, request.RequestId));
                return result;
            }

            IWotPreparedViewPublication? views = null;
            try
            {
                if (capture.ViewUpdates.Count != 0 || capture.ViewRemovals.Count != 0)
                {
                    if (m_sourceViewHost is not IWotPreparedViewProjectionHost
                        { SupportsPreparedPublication: true } viewHost)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported, "The View host cannot join the prepared publication.");
                    }
                    views = await viewHost.PrepareAsync(
                        capture.ViewUpdates.ToArrayOf(), capture.ViewRemovals.ToArrayOf(),
                        new WotCommittedPublicationState(
                            snapshot, CommittedPublication.Views, CommittedPublication.ActiveBindingPlans),
                        cancellationToken).ConfigureAwait(false);
                }

                IWotPreparedProjectionPublication prepared = await host.PrepareAsync(
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
                    foreach (ClosureState state in capture.Closures.Values)
                    {
                        if (ReferenceEquals(state.Handle, change.Candidate))
                        {
                            state.Handle = bound;
                        }
                    }
                }
                ByteString graph = prepared.ViewGraph is { } preparedGraph
                    ? preparedGraph.CanonicalViewGraphState
                    : default;
                if (prepared.ViewGraph is { } graphState)
                {
                    MergeViewGraphMetadata(capture, graphState, snapshot);
                }
                uint generation = checked(snapshot.RefreshGeneration + 1);
                IWotPreparedRegistryPublication metadata = await registry.PreparePublicationAsync(
                    snapshot, capture.Projections.ToArrayOf(), generation, graph, cancellationToken)
                    .ConfigureAwait(false);
                await using var metadataLifetime = metadata.ConfigureAwait(false);
                ArrayOf<WotViewProjectionHandle> committedViews = prepared.ViewGraph is { } completeGraph
                    ? completeGraph.Views
                    : capture.Closures.Values.SelectMany(closure => closure.ViewHandles).ToArrayOf();
                var publication = new WotCommittedPublicationState(
                    metadata.IntendedSnapshot, committedViews,
                    capture.Closures.Values.SelectMany(closure => closure.BindingPlans).ToArrayOf());

                WotRegistryCommitDurabilityUncertainException? publicationWarning = null;
                await prepared.CommitAsync(
                    async token =>
                    {
                        try
                        {
                            await metadata.DecideAsync(token).ConfigureAwait(false);
                        }
                        catch (WotRegistryCommitDurabilityUncertainException warning) when (metadata.IsCommitted)
                        {
                            publicationWarning = warning;
                        }
                    },
                    () =>
                    {
                        m_closures.Clear();
                        foreach (KeyValuePair<string, ClosureState> entry in capture.Closures)
                        {
                            m_closures.Add(entry.Key, entry.Value);
                        }
                        m_projectionNamespaceUris.Clear();
                        m_projectionNamespaceUris.UnionWith(capture.Namespaces);
                        m_generation = generation;
                        Volatile.Write(ref m_committedPublication, publication);
                        try
                        {
                            views?.OnPublished(publication);
                        }
                        finally
                        {
                            // Changed observers must see the complete committed image.
                            try
                            {
                                metadata.Publish();
                            }
                            catch (WotRegistryCommitDurabilityUncertainException warning) when (metadata.IsCommitted)
                            {
                                publicationWarning = warning;
                            }
                        }
                    },
                    cancellationToken).ConfigureAwait(false);

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
                staged.Summary.Generation = generation;
                WotRegistryCommitDurabilityUncertainException? durabilityWarning =
                    publicationWarning ?? metadata.DurabilityWarning;
                if (durabilityWarning is not null)
                {
                    AddCommittedWarning(staged,
                        "Committed durability warning: " + durabilityWarning.Message + " " +
                        DescribeCommittedFailure(durabilityWarning.PersistenceFailure));
                }
                if (prepared.CleanupFailure is { } cleanupFailure)
                {
                    AddCommittedWarning(staged,
                        "Committed reconciliation warning: " + DescribeCommittedFailure(cleanupFailure));
                }
                var committed = new WotRefreshResult(staged.Summary, staged.Results, generation);
                foreach (WotMaterializationEventArgs change in capture.Events)
                {
                    if (change.Kind != WotMaterializationEventKind.RefreshCompleted)
                    {
                        RaiseCommittedEvent(CopyEvent(change, generation, request.RequestId), committed);
                    }
                }
                RaiseCommittedEvent(CreateCompletion(committed, request.RequestId), committed);
                return committed;
            }
            finally
            {
                if (views is not null)
                {
                    await views.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static string DescribeCommittedFailure(Exception failure)
        {
            return failure is AggregateException aggregate
                ? string.Join(" ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
                : failure.Message;
        }

        private void RaiseCommittedEvent(WotMaterializationEventArgs change, WotRefreshResult committed)
        {
            EventHandler<WotMaterializationEventArgs>? observers = Event;
            if (observers is null)
            {
                return;
            }
            foreach (EventHandler<WotMaterializationEventArgs> observer in observers.GetInvocationList())
            {
                try
                {
                    observer(this, change);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    AddCommittedWarning(committed,
                        "Committed notification warning: " + DescribeCommittedFailure(failure));
                }
            }
        }

        private static void AddCommittedWarning(WotRefreshResult result, string message)
        {
            foreach (WoTResourceLoadResultDataType row in result.Results)
            {
                if (row.Outcome is WoTOutcomeEnum.Success or WoTOutcomeEnum.Warning)
                {
                    row.Outcome = WoTOutcomeEnum.Warning;
                    row.Message = (row.Message ?? string.Empty) + " " + message;
                }
            }
            result.Summary.Outcome = WoTOutcomeEnum.Warning;
        }

        private static void MergeViewGraphMetadata(
            PublicationCapture capture, WotPreparedViewGraphState graph, WotRegistrySnapshot snapshot)
        {
            var handles = new Dictionary<string, WotViewProjectionHandle>(StringComparer.Ordinal);
            foreach (WotViewProjectionHandle handle in graph.Views)
            {
                handles.Add(handle.ResourceXid, handle);
            }
            foreach (string xid in graph.AffectedResourceXids)
            {
                WotResource? resource = snapshot.FindResourceByXid(xid);
                if (resource is null)
                {
                    continue;
                }
                handles.TryGetValue(xid, out WotViewProjectionHandle? handle);
                WotResourceProjection? previous = capture.Projections.FirstOrDefault(projection =>
                    projection.GroupId == resource.GroupId && projection.ResourceId == resource.ResourceId);
                capture.Projections.RemoveAll(projection =>
                    projection.GroupId == resource.GroupId && projection.ResourceId == resource.ResourceId);
                capture.Projections.Add(new WotResourceProjection(
                    resource.GroupId, resource.ResourceId,
                    handle is null ? WoTLoadStateEnum.Unloaded : WoTLoadStateEnum.Active,
                    handle is null ? null : previous is null ? resource.ActiveVersionId : previous.ActiveVersionId,
                    checked(snapshot.RefreshGeneration + 1),
                    handle?.MaterializedNodeCount ?? 0,
                    handle is null ? default : handle.ViewNodeId,
                    previous is null ? resource.Validation : previous.Validation,
                    previous is null ? resource.Diagnostics : previous.Diagnostics,
                    DateTime.UtcNow)
                {
                    VersionId = previous?.VersionId ?? resource.ActiveVersionId ?? resource.DefaultVersionId,
                    RetainPreviousActiveVersion = previous?.RetainPreviousActiveVersion ?? false,
                    DependencySnapshot = previous?.DependencySnapshot,
                    LastDependencyAttempt = previous?.LastDependencyAttempt
                });
            }
        }

        private static WotRefreshResult NormalizeUncommitted(
            WotRefreshResult staged, WotRegistrySnapshot snapshot, bool failed, bool dryRun)
        {
            uint generation = snapshot.RefreshGeneration;
            foreach (WoTResourceLoadResultDataType row in staged.Results)
            {
                row.Generation = generation;
                if (failed && row.Outcome is WoTOutcomeEnum.Success or WoTOutcomeEnum.Warning)
                {
                    WotResource? previous = row.GroupId is { } groupId && row.ResourceId is { } resourceId
                        ? snapshot.FindResource(groupId, resourceId)
                        : null;
                    row.Outcome = WoTOutcomeEnum.Failed;
                    row.LoadState = previous?.LoadState ?? WoTLoadStateEnum.Unloaded;
                    row.RootNodeId = previous is null ? default : previous.RootNodeId;
                    row.Message = "The PerRegistry publication was not committed because another member failed.";
                }
            }
            staged.Summary.Generation = generation;
            if (failed)
            {
                staged.Summary.Outcome = WoTOutcomeEnum.Failed;
                staged.Summary.Succeeded = 0;
                staged.Summary.Failed = (uint)staged.Results.Count(result => result.Outcome == WoTOutcomeEnum.Failed);
            }
            if (dryRun)
            {
                staged.Summary.Retired = 0;
            }
            return new WotRefreshResult(staged.Summary, staged.Results, generation);
        }

        private static WotMaterializationEventArgs CreateCompletion(WotRefreshResult result, string? requestId)
        {
            return new WotMaterializationEventArgs(WotMaterializationEventKind.RefreshCompleted)
            {
                Generation = result.NewGeneration,
                RequestId = requestId ?? string.Empty,
                Outcome = result.Summary.Outcome,
                Summary = result.Summary
            };
        }

        private static WotMaterializationEventArgs CopyEvent(
            WotMaterializationEventArgs source, uint generation, string? requestId)
        {
            return new WotMaterializationEventArgs(source.Kind)
            {
                Xid = source.Xid,
                ResourceId = source.ResourceId,
                VersionId = source.VersionId,
                DocumentKind = source.DocumentKind,
                Generation = generation,
                Phase = source.Phase,
                Outcome = source.Outcome,
                LoadState = source.LoadState,
                Validation = source.Validation,
                FailedNodeId = source.FailedNodeId,
                BindingUri = source.BindingUri,
                Reason = source.Reason,
                Summary = source.Summary,
                RequestId = source.RequestId.Length == 0 ? requestId ?? string.Empty : source.RequestId
            };
        }

        private sealed class PublicationCapture
        {
            public PublicationCapture(
                Dictionary<string, ClosureState> current,
                HashSet<string> namespaces)
            {
                Closures = current.ToDictionary(
                    entry => entry.Key,
                    entry => new ClosureState
                    {
                        Key = entry.Value.Key,
                        Handle = entry.Value.Handle,
                        AggregateDigest = entry.Value.AggregateDigest,
                        Generation = entry.Value.Generation,
                        MemberXids = entry.Value.MemberXids,
                        Members = entry.Value.Members,
                        ModelNamespaceUris = entry.Value.ModelNamespaceUris,
                        BindingPlans = entry.Value.BindingPlans,
                        ViewHandles = entry.Value.ViewHandles
                    },
                    StringComparer.Ordinal);
                Namespaces = new HashSet<string>(namespaces, StringComparer.Ordinal);
            }

            public Dictionary<string, ClosureState> Closures { get; }
            public HashSet<string> Namespaces { get; }
            public List<CapturedProjection> Changes { get; } = [];
            public List<WotViewProjectionRequest> ViewUpdates { get; } = [];
            public List<WotViewProjectionHandle> ViewRemovals { get; } = [];
            public List<BindingAction> Bindings { get; } = [];
            public List<WotMaterializationEventArgs> Events { get; } = [];
            public List<WotResourceProjection> Projections { get; } = [];
        }

        private sealed record CapturedProjection(WotProjectionChange Change, WotProjectionHandle? Candidate);
        private sealed record BindingAction(WotBindingPlan Plan, bool Activate);

        private sealed class ProjectionCaptureAdapter(
            IWotProjectionHost inner, Func<PublicationCapture?> capture) : IWotProjectionHost
        {
            public ValueTask<WotProjectionHandle> AddAsync(
                WotProjectionDocument document, CancellationToken cancellationToken = default)
            {
                return capture() is { } current
                    ? Record(current, WotProjectionChange.Add(document))
                    : inner.AddAsync(document, cancellationToken);
            }

            public ValueTask<WotProjectionHandle> ShadowReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                return capture() is { } pending
                    ? Record(pending, WotProjectionChange.Replace(current, document))
                    : inner.ShadowReloadAsync(current, document, cancellationToken);
            }

            public ValueTask<WotProjectionHandle> ImmediateReloadAsync(
                WotProjectionHandle current, WotProjectionDocument document,
                CancellationToken cancellationToken = default)
            {
                return capture() is { } pending
                    ? Record(pending, WotProjectionChange.Replace(
                        current, document, WotProjectionRetirementPolicy.Immediate))
                    : inner.ImmediateReloadAsync(current, document, cancellationToken);
            }

            public ValueTask RemoveAsync(WotProjectionHandle handle, CancellationToken cancellationToken = default)
            {
                if (capture() is not { } pending)
                {
                    return inner.RemoveAsync(handle, cancellationToken);
                }
                pending.Changes.Add(new CapturedProjection(WotProjectionChange.Remove(handle), null));
                return default;
            }

            private static ValueTask<WotProjectionHandle> Record(
                PublicationCapture capture, WotProjectionChange change)
            {
                var candidate = new WotProjectionHandle(
                    change.Document!.ClosureKey, (change.Current?.Generation ?? 0) + 1, null, [], 0);
                capture.Changes.Add(new CapturedProjection(change, candidate));
                return new ValueTask<WotProjectionHandle>(candidate);
            }
        }

        private sealed class ViewCaptureAdapter(
            IWotViewProjectionHost inner, Func<PublicationCapture?> capture) : IWotViewProjectionHost
        {
            public ValueTask<WotViewProjectionHandle> ApplyAsync(
                WotViewProjectionRequest request, CancellationToken cancellationToken = default)
            {
                if (capture() is not { } pending)
                {
                    return inner.ApplyAsync(request, cancellationToken);
                }
                pending.ViewUpdates.Add(request);
                return new ValueTask<WotViewProjectionHandle>(new WotViewProjectionHandle(
                    request.ResourceXid, request.ViewNodeId, request.Plan.MaterializedNodeCount,
                    omissions: request.Plan.Omissions));
            }

            public ValueTask RemoveAsync(
                WotViewProjectionHandle handle, CancellationToken cancellationToken = default)
            {
                if (capture() is not { } pending)
                {
                    return inner.RemoveAsync(handle, cancellationToken);
                }
                pending.ViewRemovals.Add(handle);
                return default;
            }
        }

        private sealed class BinderCaptureAdapter(
            IWotBinderRegistry inner, Func<PublicationCapture?> capture) : IWotBinderRegistry
        {
            public IReadOnlyList<WoTBindingCapabilityDataType> Capabilities => inner.Capabilities;
            public WotBindingPlan Prepare(WotBindingPlanRequest request) => inner.Prepare(request);

            public ValueTask ActivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            {
                if (capture() is not { } pending)
                {
                    return inner.ActivateAsync(plan, cancellationToken);
                }
                pending.Bindings.Add(new BindingAction(plan, true));
                return default;
            }

            public ValueTask DeactivateAsync(WotBindingPlan plan, CancellationToken cancellationToken = default)
            {
                if (capture() is not { } pending)
                {
                    return inner.DeactivateAsync(plan, cancellationToken);
                }
                pending.Bindings.Add(new BindingAction(plan, false));
                return default;
            }
        }

        private readonly IWotProjectionHost m_sourceHost;
        private readonly IWotViewProjectionHost m_sourceViewHost;
        private readonly IWotBinderRegistry m_sourceBinders;
        private PublicationCapture? m_preparing;
        private WotCommittedPublicationState m_committedPublication;
    }
}
