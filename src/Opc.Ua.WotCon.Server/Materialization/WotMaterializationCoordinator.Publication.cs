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

        /// <summary>
        /// Gets a detached final rechecked plan, or null before an actual invocation has planned publication.
        /// Dry runs do not replace this diagnostic value.
        /// </summary>
        public WoTRefreshPlanDataType? LastRefreshPlan => CoreUtils.Clone(Volatile.Read(ref m_lastRefreshPlan));

        /// <summary>
        /// Gets the actual invocation-isolated modes supported by the configured owners.
        /// Legacy immediate projection is not advertised as this capability.
        /// </summary>
        public ArrayOf<WoTAtomicityEnum> SupportedAtomicities =>
            m_registry is IWotInvocationRegistryPublicationService { SupportsPreparedPublication: true } &&
                m_sourceHost is IWotInvocationProjectionHost source
                ? [.. source.SupportedAtomicities] : [];

        private Dictionary<string, ClosureState> ClosureStates => m_preparing?.Closures ?? m_closures;
        private HashSet<string> ProjectionNamespaces => m_preparing?.Namespaces ?? m_projectionNamespaceUris;

        private async ValueTask<WotRefreshResult> RefreshPreparedUnitsAsync(
            WotRefreshCapture refresh,
            WotRegistrySnapshot snapshot,
            DateTime start,
            IWotProjectionPublication? publication,
            IWotRegistryPublication? registryPublication,
            CancellationToken cancellationToken)
        {
            WotPublicationPlan plan = WotPublicationPlanner.Create(
                refresh.Inputs.Closures, refresh.Request.Atomicity,
                m_closures.Values.Select(closure => new WotPublicationFootprint(
                    closure.Members.Select(member => member.Xid).ToArrayOf(), closure.MemberXids.ToArrayOf()))
                    .ToArrayOf());
            if (!refresh.Request.DryRun)
            {
                if (publication is null || registryPublication is null)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "Invocation isolation is required.");
                }
                Volatile.Write(ref m_lastRefreshPlan,
                    refresh.CreateRefreshPlan(plan.AppliedAtomicity, (uint)plan.Units.Count));
            }
            var rows = ImmutableArray.CreateBuilder<WoTResourceLoadResultDataType>();
            var satisfied = new HashSet<string>(StringComparer.Ordinal);
            var activation = new HashSet<string>(
                refresh.Inputs.Closures.ToList().SelectMany(closure => closure.ActivationMembers.ToList())
                    .Select(member => member.Xid), StringComparer.Ordinal);
            uint retired = 0;
            bool committedWarning = false;
            bool includeSkipped = true;
            for (int i = 0; i < plan.Units.Count; i++)
            {
                ArrayOf<WotDependencyClosure> unit = plan.Units[i];
                var unitXids = new HashSet<string>(unit.ToList()
                    .SelectMany(closure => closure.ActivationMembers.ToList()).Select(member => member.Xid),
                    StringComparer.Ordinal);
                bool blocked = unit.ToList().SelectMany(closure => closure.Dependencies).Any(edge =>
                    edge.Resolved && edge.TargetXid is { } target && activation.Contains(target) &&
                    !unitXids.Contains(target) && !satisfied.Contains(target));
                if (blocked)
                {
                    foreach (WotResource member in unit.ToList()
                        .SelectMany(closure => closure.ActivationMembers.ToList()))
                    {
                        rows.Add(FailResult(member, m_generation, WoTPhaseEnum.DependencyResolution,
                            "An exact required activation prerequisite did not commit."));
                    }
                    continue;
                }
                using WotRefreshCapture unitCapture = refresh.ForPublication(unit);
                WotRefreshResult result;
                try
                {
                    result = await RefreshPreparedUnitAsync(
                        unitCapture, snapshot, start, unit, includeSkipped,
                        publication, registryPublication, cancellationToken).ConfigureAwait(false);
                }
                catch (WotRegistryCommitNotCommittedException failure) when (
                    rows.Any(row => row.Outcome is WoTOutcomeEnum.Success or WoTOutcomeEnum.Warning))
                {
                    ImmutableArray<WoTResourceLoadResultDataType> rejected = unit.ToList()
                        .SelectMany(closure => closure.ActivationMembers.ToList())
                        .Select(member => FailResult(member, m_generation, WoTPhaseEnum.Activation,
                            "The store confirmed that this unit did not commit: " + failure.Message))
                        .ToImmutableArray();
                    result = new WotRefreshResult(new WoTRefreshSummaryDataType
                    {
                        Generation = m_generation,
                        Outcome = WoTOutcomeEnum.Failed,
                        Total = (uint)rejected.Length,
                        Failed = (uint)rejected.Length
                    }, rejected, m_generation);
                }
                rows.AddRange(result.Results);
                committedWarning |= result.Summary.Outcome == WoTOutcomeEnum.Warning;
                if (result.Summary.Failed == 0)
                {
                    satisfied.UnionWith(unitXids);
                }
                retired += result.Summary.Retired;
                snapshot = m_registry.Current;
                includeSkipped = false;
            }
            if (plan.Units.IsEmpty)
            {
                WotRefreshResult empty = await RefreshPreparedUnitAsync(
                    refresh, snapshot, start, [], true, publication, registryPublication, cancellationToken)
                    .ConfigureAwait(false);
                rows.AddRange(empty.Results);
                retired = empty.Summary.Retired;
            }
            uint succeeded = (uint)rows.Count(row => row.Outcome is WoTOutcomeEnum.Success or WoTOutcomeEnum.Warning);
            uint failed = (uint)rows.Count(row => row.Outcome == WoTOutcomeEnum.Failed);
            foreach (WoTResourceLoadResultDataType row in rows)
            {
                if (row.Outcome == WoTOutcomeEnum.Failed)
                {
                    row.Generation = m_generation;
                }
            }
            bool warning = committedWarning || rows.Any(row => row.Outcome == WoTOutcomeEnum.Warning);
            var summary = new WoTRefreshSummaryDataType
            {
                RequestId = refresh.Request.RequestId,
                Generation = m_generation,
                Atomicity = plan.AppliedAtomicity,
                Outcome = failed != 0
                    ? succeeded != 0 ? WoTOutcomeEnum.Warning : WoTOutcomeEnum.Failed
                    : warning ? WoTOutcomeEnum.Warning
                    : succeeded != 0 ? WoTOutcomeEnum.Success : WoTOutcomeEnum.Unchanged,
                StartTime = start,
                EndTime = DateTime.UtcNow,
                Total = (uint)rows.Count,
                Succeeded = succeeded,
                Failed = failed,
                Unchanged = (uint)rows.Count(row => row.Outcome == WoTOutcomeEnum.Unchanged),
                Skipped = (uint)rows.Count(row => row.Outcome == WoTOutcomeEnum.Skipped),
                Retired = retired
            };
            var completed = new WotRefreshResult(summary, rows.ToImmutable(), m_generation);
            if (succeeded != 0 && !refresh.Request.DryRun)
            {
                RaiseCommittedEvent(CreateCompletion(completed, refresh.Request.RequestId), completed);
            }
            else
            {
                RaiseEvent(CreateCompletion(completed, refresh.Request.RequestId));
            }
            return completed;
        }

        private async ValueTask<WotRefreshResult> RefreshPreparedUnitAsync(
            WotRefreshCapture refresh,
            WotRegistrySnapshot snapshot,
            DateTime start,
            ArrayOf<WotDependencyClosure> unit,
            bool includeSkipped,
            IWotProjectionPublication? publication,
            IWotRegistryPublication? registryPublication,
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
                staged = await RefreshCoreAsync(refresh, start, cancellationToken, unit, includeSkipped)
                    .ConfigureAwait(false);
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
                if (failed)
                {
                    var complete = staged.Results.ToBuilder();
                    foreach (WotResource member in unit.ToList()
                        .SelectMany(closure => closure.ActivationMembers.ToList()))
                    {
                        if (!complete.Any(row => row.GroupId == member.GroupId && row.ResourceId == member.ResourceId))
                        {
                            complete.Add(FailResult(member, m_generation, WoTPhaseEnum.Activation,
                                "The publication unit was not committed because another member failed."));
                        }
                    }
                    staged.Summary.Total = (uint)complete.Count;
                    staged = new WotRefreshResult(staged.Summary, complete.ToImmutable(), m_generation);
                }
                WotRefreshResult result = NormalizeUncommitted(staged, snapshot, failed, dryRun);
                if (!dryRun && registryPublication is not null && capture.Projections.Count != 0)
                {
                    await PublishCompletedAttemptsAsync(
                        capture, snapshot, result, registryPublication, cancellationToken).ConfigureAwait(false);
                }
                foreach (WotMaterializationEventArgs change in capture.Events)
                {
                    if (!dryRun && change.Kind is (WotMaterializationEventKind.ValidationFailure or
                        WotMaterializationEventKind.LoadFailure or WotMaterializationEventKind.BindingFailure))
                    {
                        RaiseEvent(CopyEvent(change, m_generation, request.RequestId));
                    }
                }
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

                IWotPreparedProjectionPublication prepared = publication is null
                    ? await host.PrepareAsync(
                        capture.Changes.Select(change => change.Change).ToArrayOf(), views, cancellationToken)
                        .ConfigureAwait(false)
                    : await publication.PrepareAsync(
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
                BindPreparedSourceRoots(capture, staged, snapshot);
                ByteString graph = prepared.ViewGraph is { } preparedGraph
                    ? preparedGraph.CanonicalViewGraphState
                    : default;
                if (prepared.ViewGraph is { } graphState)
                {
                    MergeViewGraphMetadata(capture, graphState, snapshot, unit, staged);
                }
                foreach (ClosureState closure in capture.Closures.Values)
                {
                    closure.PublishedMetadata = [.. closure.PublishedMetadata.Select(projection =>
                        capture.Projections.FirstOrDefault(candidate =>
                            candidate.GroupId == projection.GroupId && candidate.ResourceId == projection.ResourceId)
                        ?? projection)];
                    if (prepared.ViewGraph is { } complete)
                    {
                        closure.ViewHandles = [.. complete.Views.ToList().Where(view =>
                            closure.MemberXids.Contains(view.ResourceXid, StringComparer.Ordinal))];
                    }
                }
                uint generation = checked(snapshot.RefreshGeneration + 1);
                IWotPreparedRegistryPublication metadata = registryPublication is null
                    ? await registry.PreparePublicationAsync(
                        snapshot, capture.Projections.ToArrayOf(), generation, graph, cancellationToken)
                        .ConfigureAwait(false)
                    : await registryPublication.PrepareAsync(
                        snapshot, capture.Projections.ToArrayOf(), generation, graph, cancellationToken)
                        .ConfigureAwait(false);
                await using var metadataLifetime = metadata.ConfigureAwait(false);
                ArrayOf<WotViewProjectionHandle> committedViews = prepared.ViewGraph is { } completeGraph
                    ? completeGraph.Views
                    : capture.Closures.Values.SelectMany(closure => closure.ViewHandles).ToArrayOf();
                var committedPublication = new WotCommittedPublicationState(
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
                        Volatile.Write(ref m_committedPublication, committedPublication);
                        try
                        {
                            views?.OnPublished(committedPublication);
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

        private async ValueTask PublishCompletedAttemptsAsync(
            PublicationCapture capture,
            WotRegistrySnapshot snapshot,
            WotRefreshResult result,
            IWotRegistryPublication registryPublication,
            CancellationToken cancellationToken)
        {
            var observations = new List<WotResourceProjection>();
            foreach (WotResourceProjection projection in capture.Projections)
            {
                WotResource? previous = snapshot.FindResource(projection.GroupId, projection.ResourceId);
                if (previous is null)
                {
                    continue;
                }
                WotDependencySnapshot? attempted = projection.LastDependencyAttempt;
                WotDependencySnapshot? attempt = attempted is null ? null : new WotDependencySnapshot(
                    attempted.SourceVersionId, m_generation, attempted.RequestId, attempted.ResolvedAt, false,
                    attempted.EffectiveInputDigest, attempted.Edges, attempted.Targets);
                bool active = previous.ActiveVersionId is not null && m_closures.Values.Any(closure =>
                    closure.Members.Any(member => member.Xid == previous.Xid &&
                        member.VersionId == previous.ActiveVersionId));
                observations.Add(new WotResourceProjection(
                    projection.GroupId, projection.ResourceId,
                    active ? WoTLoadStateEnum.Active :
                        previous.ActiveVersionId is null && projection.LoadState == WoTLoadStateEnum.Failed
                            ? WoTLoadStateEnum.Failed : previous.LoadState,
                    previous.ActiveVersionId, previous.RefreshGeneration, previous.MaterializedNodeCount,
                    previous.RootNodeId, projection.Validation, projection.Diagnostics, DateTime.UtcNow)
                {
                    RetainPreviousActiveVersion = true,
                    VersionId = projection.VersionId,
                    LastDependencyAttempt = attempt
                });
            }
            if (observations.Count == 0)
            {
                return;
            }
            IWotPreparedRegistryPublication prepared = await registryPublication.PrepareAsync(
                snapshot, observations.ToArrayOf(), m_generation, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await using var preparedLifetime = prepared.ConfigureAwait(false);
            await prepared.DecideAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref m_committedPublication, new WotCommittedPublicationState(
                prepared.IntendedSnapshot, CommittedPublication.Views, CommittedPublication.ActiveBindingPlans));
            WotRegistryCommitDurabilityUncertainException? warning = prepared.DurabilityWarning;
            try
            {
                prepared.Publish();
            }
            catch (WotRegistryCommitDurabilityUncertainException failure) when (prepared.IsCommitted)
            {
                warning = failure;
            }
            if (warning is not null)
            {
                string message = "Completed-attempt metadata committed with a warning: " +
                    DescribeCommittedFailure(warning.PersistenceFailure);
                foreach (WoTResourceLoadResultDataType row in result.Results)
                {
                    row.Message = (row.Message ?? string.Empty) + " " + message;
                }
                if (result.Summary.Failed == 0)
                {
                    result.Summary.Outcome = WoTOutcomeEnum.Warning;
                }
            }
        }

        private static void CheckExpectedGeneration(WotCapturedRefreshRequest request, uint generation)
        {
            if (request.ExpectedGeneration != 0 && request.ExpectedGeneration != generation)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState, "The expected refresh generation is stale.");
            }
        }

        private bool IsCurrentCapture(WotRefreshCapture capture)
        {
            return capture.MaxJsonDepth == m_converterOptions.MaxJsonDepth &&
                capture.DocumentSetMode == m_converterOptions.DocumentSetMode &&
                capture.ProjectionCompatibilityMode == m_converterOptions.ProjectionCompatibilityMode &&
                capture.BinderRevision == BinderVersion &&
                capture.StrictBindings == StrictBindings &&
                capture.RetirementPolicy == RetirementPolicy &&
                ReferenceEquals(capture.RegistryOrigin, RegistryOrigin);
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

        private void BindPreparedSourceRoots(
            PublicationCapture capture, WotRefreshResult staged, WotRegistrySnapshot snapshot)
        {
            for (int i = 0; i < capture.Projections.Count; i++)
            {
                WotResourceProjection projection = capture.Projections[i];
                WotResource? resource = snapshot.FindResource(projection.GroupId, projection.ResourceId);
                if (resource is null || !capture.SourceRoots.TryGetValue(resource.Xid, out ExpandedNodeId root))
                {
                    continue;
                }
                NodeId nodeId = ResolveRootNodeId(root);
                if (nodeId.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "A prepared source root cannot be resolved in its native image.");
                }
                capture.Projections[i] = new WotResourceProjection(
                    projection.GroupId, projection.ResourceId, projection.LoadState, projection.ActiveVersionId,
                    projection.RefreshGeneration, projection.MaterializedNodeCount, nodeId,
                    projection.Validation, projection.Diagnostics, projection.LastRefreshTime)
                {
                    VersionId = projection.VersionId,
                    RetainPreviousActiveVersion = projection.RetainPreviousActiveVersion,
                    DependencySnapshot = projection.DependencySnapshot,
                    LastDependencyAttempt = projection.LastDependencyAttempt
                };
                foreach (WoTResourceLoadResultDataType row in staged.Results)
                {
                    if (row.Xid == resource.Xid)
                    {
                        row.RootNodeId = nodeId;
                    }
                }
            }
        }

        private static void MergeViewGraphMetadata(
            PublicationCapture capture,
            WotPreparedViewGraphState graph,
            WotRegistrySnapshot snapshot,
            ArrayOf<WotDependencyClosure> unit,
            WotRefreshResult staged)
        {
            var permitted = new HashSet<string>(
                unit.ToList().SelectMany(closure => closure.ActivationMembers.ToList()).Select(member => member.Xid),
                StringComparer.Ordinal);
            permitted.UnionWith(capture.ViewRemovals.Select(handle => handle.ResourceXid));
            var handles = new Dictionary<string, WotViewProjectionHandle>(StringComparer.Ordinal);
            foreach (WotViewProjectionHandle handle in graph.Views)
            {
                handles.Add(handle.ResourceXid, handle);
            }
            foreach (string xid in graph.AffectedResourceXids)
            {
                if (!permitted.Contains(xid))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "The prepared View graph affects a Resource outside its publication unit.");
                }
                WotResource? resource = snapshot.FindResourceByXid(xid);
                if (resource is null)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                        "A prepared View Resource does not belong to the authoritative registry snapshot.");
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
                foreach (WoTResourceLoadResultDataType row in staged.Results)
                {
                    if (row.Xid != xid)
                    {
                        continue;
                    }
                    row.RootNodeId = handle is null ? NodeId.Null : handle.ViewNodeId;
                    row.MaterializedNodeCount = (uint)(handle?.MaterializedNodeCount ?? 0);
                    if (handle is not null && !handle.Omissions.IsEmpty)
                    {
                        row.Outcome = WoTOutcomeEnum.Warning;
                        row.Message = handle.Message;
                    }
                }
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
                    row.Phase = WoTPhaseEnum.Activation;
                    row.LoadState = previous?.LoadState ?? WoTLoadStateEnum.Unloaded;
                    row.RootNodeId = previous is null ? default : previous.RootNodeId;
                    row.Message = "The publication unit was not committed because another member failed.";
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
                        PublishedMetadata = entry.Value.PublishedMetadata,
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
            public Dictionary<string, ExpandedNodeId> SourceRoots { get; } = new(StringComparer.Ordinal);
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
        private WoTRefreshPlanDataType? m_lastRefreshPlan;
    }
}
