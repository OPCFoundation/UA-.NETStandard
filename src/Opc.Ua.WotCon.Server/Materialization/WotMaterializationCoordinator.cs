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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Coordinates projecting registry documents into the AddressSpace. It parses
    /// and validates each document with <see cref="Wot"/>, builds the TD/TM
    /// dependency closures, converts each closure to one or more NodeSet2
    /// documents and projects them through the <see cref="IWotProjectionHost"/>
    /// (runtime NodeSet Add for first activation, ShadowReload for updates). The
    /// stable registry NodeManager is kept separate. Independent closures commit
    /// independently; a failed or invalid closure retains its previous active
    /// generation. An unchanged closure (same digest, options and binder version)
    /// returns <see cref="WoTOutcomeEnum.Unchanged"/> and emits no model change.
    /// </summary>
    public sealed partial class WotMaterializationCoordinator : IWotRefreshCaptureProvider, IDisposable
    {
        /// <summary>
        /// Initializes a new coordinator.
        /// </summary>
        public WotMaterializationCoordinator(
            IWotRegistryService registry,
            IWotProjectionHost projectionHost,
            IWotBinderRegistry? binderRegistry = null,
            WotNodeSetConverterOptions? converterOptions = null,
            IWotDocumentConverter? documentConverter = null,
            IEnumerable<IWotNodeSetContributor>? nodeSetContributors = null,
            IWotNodeSetResolver? nodeSetResolver = null,
            IWotViewProjectionHost? viewProjectionHost = null)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_sourceHost = projectionHost ?? throw new ArgumentNullException(nameof(projectionHost));
            m_host = new ProjectionCaptureAdapter(m_sourceHost, () => m_preparing);
            m_sourceBinders = binderRegistry ?? NullWotBinderRegistry.Instance;
            m_binders = new BinderCaptureAdapter(m_sourceBinders, () => m_preparing);
            m_converterOptions = converterOptions ?? new WotNodeSetConverterOptions();
            m_converterOptions.Validate();
            m_converter = documentConverter
                ?? new WotNodeSetDocumentConverter(m_converterOptions);
            m_nodeSetContributors = nodeSetContributors is null
                ? []
                : [.. nodeSetContributors];
            m_nodeSetResolver = nodeSetResolver;
            m_sourceViewHost = viewProjectionHost ?? new InMemoryWotViewProjectionHost();
            m_viewHost = new ViewCaptureAdapter(m_sourceViewHost, () => m_preparing);
            m_committedPublication = new WotCommittedPublicationState(m_registry.Current);
            m_generation = m_registry.Current.RefreshGeneration;
        }

        /// <summary>
        /// Raised for each materialization event (resource / validation / load / refresh).
        /// </summary>
        public event EventHandler<WotMaterializationEventArgs>? Event;

        /// <summary>
        /// Gets the current refresh generation.
        /// </summary>
        public uint Generation => m_generation;

        /// <inheritdoc/>
        public async ValueTask<WotRefreshCapture> CaptureAsync(
            WotRefreshRequest request,
            CancellationToken cancellationToken = default)
        {
            WotCapturedRefreshRequest invocation = WotCapturedRefreshRequest.Capture(request);
            if (!TryBeginOperation(allowDisposed: false))
            {
                throw new ObjectDisposedException(nameof(WotMaterializationCoordinator));
            }
            try
            {
                await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (invocation.ExpectedGeneration != 0 && invocation.ExpectedGeneration != m_generation)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidState, "The expected refresh generation is stale.");
                    }
                    return await CaptureInputsAsync(invocation, cancellationToken).ConfigureAwait(false);
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

        /// <summary>
        /// Refreshes (re-projects) the registry into the AddressSpace and returns
        /// the detailed result.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        public async ValueTask<WotRefreshResult> RefreshAsync(
            WotRefreshRequest request,
            CancellationToken cancellationToken = default)
        {
            WotCapturedRefreshRequest invocation = WotCapturedRefreshRequest.Capture(request);
            m_converterOptions.Validate();
            using CancellationTokenSource? budget = invocation.Timeout == 0
                ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (budget is not null)
            {
                budget.CancelAfter(TimeSpan.FromMilliseconds(invocation.Timeout));
                cancellationToken = budget.Token;
            }

            if (!TryBeginOperation(allowDisposed: false))
            {
                throw new ObjectDisposedException(nameof(WotMaterializationCoordinator));
            }

            try
            {
                await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    DateTime start = DateTime.UtcNow;
                    if (m_publicationRecoveryRequired)
                    {
                        if (invocation.DryRun)
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidState,
                                "Authoritative publication recovery is required before a dry run can proceed.");
                        }
                        try
                        {
                            await RecoverCommittedImageAsync(cancellationToken).ConfigureAwait(false);
                            m_publicationRecoveryRequired = false;
                        }
                        catch (Exception failure) when (failure is IOException or InvalidDataException)
                        {
                            throw new InvalidOperationException(
                                "The publication requires an authoritative recovery reload before a refresh can proceed.",
                                failure);
                        }
                    }
                    if (m_sourceHost is IWotInvocationProjectionHost invocationHost &&
                        m_registry is IWotInvocationRegistryPublicationService { SupportsPreparedPublication: true }
                            invocationRegistry)
                    {
                        if (!invocationHost.SupportedAtomicities.Contains(invocation.Atomicity))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNotSupported, "The source owner does not support the requested atomicity.");
                        }
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            WotRegistrySnapshot expectedRegistry = m_registry.Current;
                            CheckExpectedGeneration(invocation, expectedRegistry.RefreshGeneration);
                            m_generation = expectedRegistry.RefreshGeneration;
                            IWotProjectionPublicationCapture sourceCapture = invocationHost.CapturePublication();
                            using WotRefreshCapture capture = await CaptureInputsAsync(invocation, cancellationToken)
                                .ConfigureAwait(false);
                            IWotRegistryPublication registryPublication = await invocationRegistry
                                .BeginPublicationAsync(cancellationToken).ConfigureAwait(false);
                            await using var registryLifetime = registryPublication.ConfigureAwait(false);
                            CheckExpectedGeneration(invocation, registryPublication.Current.RefreshGeneration);
                            IWotProjectionPublication publication = await sourceCapture.BeginAsync(cancellationToken)
                                .ConfigureAwait(false);
                            await using var publicationLifetime = publication.ConfigureAwait(false);
                            if (!ReferenceEquals(expectedRegistry, registryPublication.Current) ||
                                capture.PreparationGeneration != registryPublication.Current.RefreshGeneration ||
                                !publication.IsCurrent || !IsCurrentCapture(capture))
                            {
                                continue;
                            }
                            return await RefreshPreparedUnitsAsync(
                                capture, expectedRegistry, start, publication, registryPublication, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported, "The configured owners cannot isolate the requested publication.");
                }
                catch (WotRegistryCommitIndeterminateException)
                {
                    m_publicationRecoveryRequired = true;
                    throw;
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

        private async ValueTask<WotRefreshResult> RefreshCoreAsync(
            WotRefreshCapture capture,
            DateTime start,
            CancellationToken cancellationToken,
            ArrayOf<WotDependencyClosure> selectedClosures = default,
            bool includeSkipped = true)
        {
            WotCapturedRefreshRequest invocation = capture.Request;
            WotMaterializationSnapshot inputs = capture.Inputs;
            bool dryRun = invocation.DryRun && m_preparing is null;
            bool force = invocation.Force;
            bool strict = capture.StrictBindings;
            WotRegistrySnapshot snapshot = inputs.Registry;
            List<WotDependencyClosure> closures = selectedClosures.IsNull
                ? inputs.Closures.ToList() : selectedClosures.ToList();
            var selectedXids = new HashSet<string>(
                inputs.Selection.ToList().Select(selected => selected.Resource.Xid), StringComparer.Ordinal);
            var contentCache = new Dictionary<string, ByteString>(inputs.Contents, StringComparer.Ordinal);

            var targetKeys = new HashSet<string>(
                closures.Select(c => c.Key), StringComparer.Ordinal);
            var inputXids = new HashSet<string>(
                inputs.Resources.ToList().Select(resource => resource.Xid), StringComparer.Ordinal);
            foreach (ClosureState tracked in ClosureStates.Values)
            {
                if ((!inputs.SelectsAll && !tracked.MemberXids.Any(selectedXids.Contains)) ||
                    tracked.MemberXids.Any(inputXids.Contains))
                {
                    targetKeys.Add(tracked.Key);
                }
            }
            var declarationContext = new WotProjectionDeclarationContext(
                snapshot, m_converterOptions.MaxJsonDepth,
                (version, token) => ReadCachedContentAsync(contentCache, version, token),
                m_converterOptions.MaxNodeCount);
            declarationContext.AddAvailableNativePartitions(
                contentCache, m_converterOptions, cancellationToken);

            uint newGeneration = m_preparing?.RecoveryGeneration ?? m_generation + 1;
            ImmutableArray<WoTResourceLoadResultDataType>.Builder results =
                ImmutableArray.CreateBuilder<WoTResourceLoadResultDataType>();
            var projections = new List<WotResourceProjection>();
            var closureOutcomes = new List<(WotDependencyClosure Closure, ClosureOutcome Outcome)>();
            int succeeded = 0;
            int unchanged = 0;
            int failed = 0;
            int skipped = 0;
            int retired = 0;

            foreach (WotSelectedResource selected in inputs.Selection)
            {
                if (!includeSkipped || (selected.Resource.Enabled && selected.Version?.HasContent == true))
                {
                    continue;
                }
                results.Add(new WoTResourceLoadResultDataType
                {
                    Xid = selected.ResultXid,
                    GroupId = selected.Resource.GroupId,
                    ResourceId = selected.Resource.ResourceId,
                    VersionId = selected.Version?.VersionId ?? string.Empty,
                    Kind = selected.Resource.Kind,
                    Outcome = WoTOutcomeEnum.Skipped,
                    Phase = WoTPhaseEnum.Fetch,
                    LoadState = selected.Resource.LoadState,
                    Generation = m_generation,
                    ContentDigest = selected.Version is null ? ByteString.Empty : selected.Version.Digest,
                    Message = "The selected Version is disabled or has no committed content."
                });
                skipped++;
            }

            // Retire tracked closures no longer desired (deleted / disabled /
            // membership changed) after their monitored items drain.
            (int retiredCount, ImmutableArray<WoTResourceLoadResultDataType> retiredResults) =
                await ReconcileRetirementsAsync(
                    targetKeys, newGeneration, dryRun, cancellationToken).ConfigureAwait(false);
            retired += retiredCount;
            skipped += retiredResults.Length;
            results.AddRange(retiredResults);

            foreach (WotDependencyClosure closure in closures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ClosureOutcome outcome = await ProcessClosureAsync(
                    capture, snapshot, closure, newGeneration, force,
                    dryRun, strict, contentCache, declarationContext, cancellationToken).ConfigureAwait(false);
                retired += outcome.Retired;

                foreach (WoTResourceLoadResultDataType result in outcome.Results)
                {
                    WotSelectedResource? selected = inputs.Selection.ToList().FirstOrDefault(candidate =>
                        candidate.Resource.Xid == result.Xid);
                    if (selected is not null)
                    {
                        result.Xid = selected.ResultXid;
                    }
                    results.Add(result);
                    switch (result.Outcome)
                    {
                        case WoTOutcomeEnum.Success:
                        case WoTOutcomeEnum.Warning:
                            succeeded++;
                            break;
                        case WoTOutcomeEnum.Unchanged:
                            unchanged++;
                            break;
                        case WoTOutcomeEnum.Skipped:
                            skipped++;
                            break;
                        default:
                            failed++;
                            break;
                    }
                }
                closureOutcomes.Add((closure, outcome));
            }

            uint observedGeneration = succeeded != 0 || retired != 0 ? newGeneration : m_generation;
            foreach ((WotDependencyClosure closure, ClosureOutcome outcome) in closureOutcomes)
            {
                if (!dryRun && capture.SupportsDependencySnapshots)
                {
                    AddDependencyObservations(
                        capture, closure, outcome, observedGeneration);
                }
                projections.AddRange(outcome.Projections);
            }

            if (!dryRun && projections.Count > 0)
            {
                if (m_preparing is { } preparing)
                {
                    preparing.Projections.AddRange(projections);
                }
                else
                {
                    await m_registry.ApplyProjectionResultsAsync(
                        projections, cancellationToken).ConfigureAwait(false);
                }
            }
            if (!dryRun && m_preparing is null && (succeeded != 0 || retired != 0))
            {
                m_generation = newGeneration;
            }

            WoTOutcomeEnum overall = failed > 0
                ? (succeeded > 0 ? WoTOutcomeEnum.Warning : WoTOutcomeEnum.Failed)
                : (succeeded > 0 ? WoTOutcomeEnum.Success : WoTOutcomeEnum.Unchanged);

            var summary = new WoTRefreshSummaryDataType
            {
                RequestId = invocation.RequestId,
                Generation = m_generation,
                Outcome = overall,
                Atomicity = invocation.Atomicity,
                StartTime = start,
                EndTime = DateTime.UtcNow,
                Total = (uint)results.Count,
                Succeeded = (uint)succeeded,
                Unchanged = (uint)unchanged,
                Failed = (uint)failed,
                Skipped = (uint)skipped,
                Retired = (uint)retired
            };

            RaiseEvent(new WotMaterializationEventArgs(
                WotMaterializationEventKind.RefreshCompleted)
            {
                Generation = m_generation,
                RequestId = invocation.RequestId,
                Outcome = overall,
                Summary = summary
            });

            return new WotRefreshResult(
                summary, results.ToImmutable(), m_generation);
        }

        /// <summary>
        /// Deletes one registry document under a WoT Connectivity delete
        /// policy and reconciles the projections the policy affected.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The registry decides what the policy does to stored state - which
        /// documents remain, which are disabled, and which are marked
        /// <c>Failed</c>. This method is what makes that decision visible in
        /// the AddressSpace: once the registry has committed, the projections
        /// that are no longer wanted are taken down and the operation is
        /// reported with the same summary and events a refresh produces, so a
        /// Client sees one story rather than two.
        /// </para>
        /// <para>
        /// A rejected delete reconciles nothing. <c>Reject</c> exists to leave
        /// state untouched, and a reconciliation pass that removed a projection
        /// would defeat exactly that.
        /// </para>
        /// </remarks>
        /// <param name="request">What to delete and under which policy.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The delete result and the projection summary.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="request"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The coordinator has been disposed.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// The configured registry does not support policy-driven deletion.
        /// </exception>
        public ValueTask<WotDeleteOutcome> DeleteAsync(
            WotDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            return ApplyResourceLifecycleAsync(
                request.GroupId, request.ResourceId, true, request.Policy, request.ExpectedEpoch,
                request.RequestId, cancellationToken);
        }

        /// <summary>
        /// Removes all live projections (used during NodeManager shutdown).
        /// </summary>
        public async ValueTask RemoveAllAsync(CancellationToken cancellationToken = default)
        {
            if (!TryBeginOperation(allowDisposed: true))
            {
                return;
            }

            try
            {
                await m_mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    foreach (ClosureState state in m_closures.Values)
                    {
                        // Deactivate bindings before removing the projection (before
                        // retirement / unload), then release the projection handle.
                        foreach (WotBindingPlan plan in state.BindingPlans)
                        {
                            await m_binders.DeactivateAsync(plan, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        foreach (WotViewProjectionHandle viewHandle in state.ViewHandles)
                        {
                            await m_viewHost.RemoveAsync(viewHandle, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        if (state.Handle is not null)
                        {
                            await m_host.RemoveAsync(state.Handle, cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                    m_closures.Clear();
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

        /// <summary>
        /// Gets or sets whether unsupported forms fail a strict closure.
        /// </summary>
        public bool StrictBindings { get; set; }

        /// <summary>
        /// Gets or sets how previous projection generations are retired after a
        /// successful version switch.
        /// </summary>
        public WotProjectionRetirementPolicy RetirementPolicy { get; set; } =
            WotProjectionRetirementPolicy.Graceful;

        /// <summary>
        /// Gets the binding capability snapshots advertised by the registered
        /// binders. These populate the registry <c>SupportedBindings</c> folder and
        /// contribute to refresh unchanged-detection.
        /// </summary>
        public IReadOnlyList<WoTBindingCapabilityDataType> BindingCapabilities => m_binders.Capabilities;

        /// <summary>
        /// Gets or sets the live server namespace table used to resolve a
        /// projection's recorded root <see cref="ExpandedNodeId"/> into a
        /// concrete server NodeId after its owning namespace is registered by
        /// the projection host. When <c>null</c>, materialized root NodeIds are
        /// not reported.
        /// </summary>
        public NamespaceTable? ServerNamespaceUris { get; set; }

        /// <summary>
        /// Gets or sets the configured immutable origin used for dependency observations.
        /// Configure an authoritative origin before refreshing to publish origin-scoped snapshots.
        /// An unconfigured coordinator does not publish dependency observations.
        /// </summary>
        public WotRegistryOrigin? RegistryOrigin { get; set; }

        /// <summary>
        /// Gets whether the owner can retain leased exact-Version observations with an authoritative origin.
        /// </summary>
        public bool SupportsDependencySnapshots =>
            RegistryOrigin is not null &&
            m_registry is IWotRegistryDependencySnapshotProvider { SupportsDependencySnapshots: true } &&
            m_registry is IWotRegistryVersionLeaseProvider;

        internal Func<WotResource, WotResourceVersion, ExpandedNodeId>? VersionNodeIdResolver { get; set; }

        /// <summary>
        /// Sets the loaded-AddressSpace half of the WoT Binding Section 5.1.5
        /// local context.
        /// </summary>
        /// <remarks>
        /// Without it a document can only bind to a type a sibling document
        /// projects, so every companion-model type binding of Section 5.2.1 is
        /// unresolvable - and Section 5.2.1 forbids falling back to
        /// <c>BaseObjectType</c>, so such a document fails to convert. A host
        /// calls this as soon as it has an <c>IServerInternal</c>. It is
        /// forwarded only to the built-in converter; a host that supplies its
        /// own <see cref="IWotDocumentConverter"/> owns its local context.
        /// </remarks>
        /// <param name="addressSpace">The AddressSpace-backed resolver.</param>
        public void UseAddressSpace(IWotNodeResolver? addressSpace)
        {
            m_addressSpace = addressSpace;
            if (m_converter is WotNodeSetDocumentConverter converter)
            {
                converter.AddressSpace = addressSpace;
            }
        }

        /// <summary>
        /// Captures the bindings participating in the currently published projection plans.
        /// The returned capability objects are detached from those plans.
        /// </summary>
        public async ValueTask<ArrayOf<WoTBindingCapabilityDataType>> GetSelectedBindingCapabilitiesAsync(
            CancellationToken cancellationToken = default)
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
                    var selected = new Dictionary<(string Uri, string? Version), WoTBindingCapabilityDataType>();
                    foreach (ClosureState closure in m_closures.Values)
                    {
                        foreach (WotBindingPlan plan in closure.BindingPlans)
                        {
                            foreach (WoTBindingCapabilityDataType capability in plan.Capabilities)
                            {
                                string uri = capability.BindingUri ?? throw new InvalidOperationException(
                                    "A selected binding has no BindingUri.");
                                selected[(uri, capability.ProfileVersion)] = capability;
                            }
                        }
                    }
                    return selected.OrderBy(pair => pair.Key.Uri, StringComparer.Ordinal)
                        .ThenBy(pair => pair.Key.Version, StringComparer.Ordinal)
                        .Select(pair => CoreUtils.Clone(pair.Value) ?? throw new InvalidOperationException(
                            "A selected binding capability could not be copied.")).ToArrayOf();
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

        /// <summary>
        /// Releases the mutex used to serialise refreshes.
        /// </summary>
        public void Dispose()
        {
            bool disposeMutex;
            lock (m_lifetimeLock)
            {
                if (m_disposed != 0)
                {
                    return;
                }
                m_disposed = 1;
                disposeMutex = TryReserveMutexDisposal();
            }
            if (disposeMutex)
            {
                m_mutex.Dispose();
            }
        }

        private bool TryBeginOperation(bool allowDisposed)
        {
            lock (m_lifetimeLock)
            {
                if (m_mutexDisposed || (!allowDisposed && m_disposed != 0))
                {
                    return false;
                }
                m_activeOperations++;
                return true;
            }
        }

        private void EndOperation()
        {
            bool disposeMutex;
            lock (m_lifetimeLock)
            {
                m_activeOperations--;
                disposeMutex = TryReserveMutexDisposal();
            }
            if (disposeMutex)
            {
                m_mutex.Dispose();
            }
        }

        private bool TryReserveMutexDisposal()
        {
            if (m_disposed == 0 ||
                m_activeOperations != 0 ||
                m_closures.Count != 0 ||
                m_mutexDisposed)
            {
                return false;
            }
            m_mutexDisposed = true;
            return true;
        }

        private static ByteString DigestOf(WotResource resource)
        {
            return resource.DefaultVersion is null ? ByteString.Empty : resource.DefaultVersion.Digest;
        }

        private async ValueTask<WotRefreshCapture> CaptureInputsAsync(
            WotCapturedRefreshRequest request,
            CancellationToken cancellationToken,
            uint? preparationGeneration = null,
            bool committedInputs = false,
            WotRegistryMutationImage? mutation = null)
        {
            m_converterOptions.Validate();
            uint generation = preparationGeneration ?? m_generation;
            WotRegistryOrigin? origin = RegistryOrigin;
            bool supportsSnapshots = origin is not null &&
                m_registry is IWotRegistryDependencySnapshotProvider { SupportsDependencySnapshots: true } &&
                m_registry is IWotRegistryVersionLeaseProvider;
            int maxJsonDepth = m_converterOptions.MaxJsonDepth;
            WotDocumentSetMode documentSetMode = m_converterOptions.DocumentSetMode;
            WotProjectionCompatibilityMode projectionCompatibilityMode = m_converterOptions.ProjectionCompatibilityMode;
            string binderRevision = BinderVersion;
            bool strictBindings = StrictBindings;
            WotProjectionRetirementPolicy retirementPolicy = RetirementPolicy;
            var versionNodeIdResolver = VersionNodeIdResolver;
            ArrayOf<ArrayOf<string>> replacementClosures =
                m_sourceHost is IWotPreparedProjectionHost { SupportsPreparedPublication: true }
                    ? m_closures.Values.Select(closure => closure.MemberXids.ToArrayOf()).ToArrayOf()
                    : [];
            WotMaterializationSnapshot inputs = await WotDependencyGraph.CapturePublicationAsync(
                m_registry, request.Selection, request.IncludeDependents, maxJsonDepth,
                replacementClosures, cancellationToken, committedInputs, mutation)
                .ConfigureAwait(false);
            bool transferred = false;
            try
            {
                var capture = new WotRefreshCapture(
                    request, generation, inputs, origin, supportsSnapshots, maxJsonDepth,
                    documentSetMode, projectionCompatibilityMode, binderRevision, strictBindings, retirementPolicy,
                    versionNodeIdResolver);
                transferred = true;
                return capture;
            }
            finally
            {
                if (!transferred)
                {
                    inputs.Dispose();
                }
            }
        }

        private static void AddDependencyObservations(
            WotRefreshCapture capture,
            WotDependencyClosure closure,
            ClosureOutcome outcome,
            uint generation)
        {
            string requestId = capture.Request.RequestId;
            ArrayOf<WotDependency> edges = closure.Dependencies.ToArrayOf();
            ArrayOf<WotDependencyTargetPin> pins = capture.GetDependencyTargets(closure);
            DateTime resolvedAt = DateTime.UtcNow;
            foreach (WoTResourceLoadResultDataType result in outcome.Results)
            {
                WotResource member = closure.Members.Single(candidate =>
                    candidate.GroupId == result.GroupId && candidate.ResourceId == result.ResourceId);
                WotResourceVersion version = member.FindVersion(result.VersionId) ??
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A dependency observation has no captured exact source Version.");
                int index = outcome.Projections.FindIndex(projection =>
                    projection.GroupId == member.GroupId && projection.ResourceId == member.ResourceId);
                WotResourceProjection projection;
                if (index >= 0)
                {
                    projection = outcome.Projections[index];
                }
                else if (result.Outcome == WoTOutcomeEnum.Unchanged)
                {
                    projection = new WotResourceProjection(
                        member.GroupId, member.ResourceId, member.LoadState, member.ActiveVersionId,
                        member.RefreshGeneration, member.MaterializedNodeCount, member.RootNodeId,
                        version.Validation, member.Diagnostics, member.LastRefreshTime)
                    {
                        VersionId = version.VersionId,
                        RetainPreviousActiveVersion = true
                    };
                }
                else
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A completed dependency attempt has no projection result.");
                }
                bool committed = result.Outcome is WoTOutcomeEnum.Success or WoTOutcomeEnum.Warning &&
                    result.LoadState == WoTLoadStateEnum.Active;
                var attempt = new WotDependencySnapshot(
                    version.VersionId, generation, requestId, resolvedAt, false,
                    outcome.EffectiveInputDigest, edges, pins);
                WotDependencySnapshot? snapshot = committed
                    ? new WotDependencySnapshot(
                        version.VersionId, generation, requestId, resolvedAt, true,
                        outcome.EffectiveInputDigest, edges, pins)
                    : null;
                var updated = new WotResourceProjection(
                    projection.GroupId, projection.ResourceId, projection.LoadState, projection.ActiveVersionId,
                    projection.RefreshGeneration, projection.MaterializedNodeCount, projection.RootNodeId,
                    projection.Validation, projection.Diagnostics, projection.LastRefreshTime)
                {
                    RetainPreviousActiveVersion = projection.RetainPreviousActiveVersion,
                    VersionId = version.VersionId,
                    DependencySnapshot = snapshot,
                    LastDependencyAttempt = attempt
                };
                if (index >= 0)
                {
                    outcome.Projections[index] = updated;
                }
                else
                {
                    outcome.Projections.Add(updated);
                }
            }
        }

        private async ValueTask<ClosureOutcome> ProcessClosureAsync(
            WotRefreshCapture capture,
            WotRegistrySnapshot snapshot,
            WotDependencyClosure closure,
            uint generation,
            bool force,
            bool dryRun,
            bool strict,
            Dictionary<string, ByteString> contentCache,
            WotProjectionDeclarationContext declarationContext,
            CancellationToken cancellationToken)
        {
            ImmutableArray<WoTResourceLoadResultDataType>.Builder results =
                ImmutableArray.CreateBuilder<WoTResourceLoadResultDataType>();
            var projections = new List<WotResourceProjection>();
            IReadOnlyList<WotResource> members = MembersOf(closure);
            IReadOnlyList<WotResource> activeMembers = closure.ActivationMembers.ToList();
            ClosureStates.TryGetValue(closure.Key, out ClosureState? tracked);
            List<ClosureState> replaced = tracked is null
                ? ClosureStates.Values.Where(existing =>
                    existing.Members.Any(owner => activeMembers.Any(member => member.Xid == owner.Xid))).ToList()
                : [];
            if (replaced.Count != 0 && m_preparing is null)
            {
                const string reason =
                    "Replacing an existing inseparable closure requires prepared-generation publication support.";
                foreach (WotResource member in activeMembers)
                {
                    results.Add(FailResult(member, generation, WoTPhaseEnum.Activation, reason));
                    projections.Add(FailProjection(member, reason));
                }
                return new ClosureOutcome(results.ToImmutable(), projections, 0);
            }
            if (replaced.Count != 0)
            {
                tracked = new ClosureState
                {
                    Key = closure.Key,
                    Handle = replaced.Select(state => state.Handle).FirstOrDefault(handle => handle is not null),
                    Members = [.. replaced.SelectMany(state => state.Members)],
                    BindingPlans = [.. replaced.SelectMany(state => state.BindingPlans)],
                    ViewHandles = [.. replaced.SelectMany(state => state.ViewHandles)]
                };
                foreach (ClosureState previous in replaced)
                {
                    if (previous.Handle is { } retiredHandle && !ReferenceEquals(retiredHandle, tracked.Handle))
                    {
                        await m_host.RemoveAsync(retiredHandle, cancellationToken).ConfigureAwait(false);
                    }
                    ClosureStates.Remove(previous.Key);
                }
            }
            var activeXids = new HashSet<string>(
                activeMembers.Select(member => member.Xid),
                StringComparer.Ordinal);
            int retiredMembers = tracked is null
                ? 0
                : tracked.Members.Count(member => !activeXids.Contains(member.Xid));
            // Unprojectable closure: cycle or missing dependency. Retain the
            // previous active generation and mark active members failed.
            if (!closure.IsProjectable)
            {
                WoTPhaseEnum phase = closure.AcquisitionFailures.IsEmpty
                    ? WoTPhaseEnum.DependencyResolution : WoTPhaseEnum.Fetch;
                string reason = string.Join("; ", closure.Diagnostics.Concat(
                    closure.AcquisitionFailures.ToList()
                        .Select(failure => failure.Resource.Xid + ": " + failure.Message)));
                foreach (WotResource member in activeMembers)
                {
                    results.Add(FailResult(member, generation, phase, reason));
                    projections.Add(FailProjection(member, reason));
                    RaiseLoadFailure(member, generation, phase, reason);
                }
                return new ClosureOutcome(
                    results.ToImmutable(),
                    projections,
                    retiredMembers);
            }

            // Project in topological (dependency-first) order.
            members = closure.OrderedResources;
            activeMembers = [.. members.Where(member => activeXids.Contains(member.Xid))];

            byte[] aggregateDigest = capture.GetRegistryInputDigest(closure).Span.ToArray();

            // Unchanged: same digest/options/binder version, and not forced.
            if (tracked?.Handle is not null &&
                !force &&
                WotContentDigest.Equal(tracked.AggregateDigest, aggregateDigest) &&
                ProjectionStateMatches(tracked, activeMembers))
            {
                foreach (WotResource member in activeMembers)
                {
                    results.Add(UnchangedResult(member, tracked.Generation));
                }
                return new ClosureOutcome(
                    results.ToImmutable(),
                    projections,
                    retiredMembers,
                    ByteString.From(aggregateDigest));
            }

            // Convert every member to a NodeSet2 source in dependency order.
            ImmutableArray<WotProjectionSource>.Builder sources = ImmutableArray.CreateBuilder<WotProjectionSource>();
            var perMemberNodeCount = new Dictionary<string, int>(StringComparer.Ordinal);
            var perMemberRoot = new Dictionary<string, ExpandedNodeId>(StringComparer.Ordinal);
            var bindingPlans = new List<WotBindingPlan>();
            var preparedPlans = new List<(WotResource Resource, WotBindingPlanRequest Request)>();
            var convertedSources = new List<(string Name, UANodeSet Nodes, ByteString Content)>();
            var projectionMembers = new List<WotResource>();
            bool degraded = false;
            var degradationReasons = new List<string>();
            var requiredNamespaces = new HashSet<string>(StringComparer.Ordinal);
            var ownedNamespaces = new HashSet<string>(StringComparer.Ordinal);
            ArrayOf<WotResource> conversionMembers = members.ToArrayOf();
            bool declarationOwnerAssigned = false;

            foreach (WotResource member in members)
            {
                WotResourceVersion? version = member.DefaultVersion;
                if (version is null)
                {
                    const string reason = "Resource has no default version.";
                    foreach (WotResource affected in activeXids.Contains(member.Xid) ? [member] : activeMembers)
                    {
                        results.Add(FailResult(
                            affected,
                            generation,
                            WoTPhaseEnum.Fetch,
                            reason));
                        projections.Add(FailProjection(affected, reason));
                        RaiseLoadFailure(affected, generation, WoTPhaseEnum.Fetch, reason);
                    }
                    return new ClosureOutcome(
                        results.ToImmutable(),
                        projections,
                        retiredMembers);
                }

                // A projection document declares affordances instead of defining
                // them: it materializes as a View that Organizes the Nodes of its
                // sources (Section 12.6), never as affordance Nodes of its own. It
                // is deferred here and materialized after its sources, whose
                // materialized roots the View resolves against.
                ByteString memberContent = await ReadCachedContentAsync(
                        contentCache, version, cancellationToken)
                    .ConfigureAwait(false);
                (bool projectionPlan, string? projectionError, bool emitsDeclarations) =
                    GetProjectionAdmission(member, version, memberContent);
                if (projectionPlan && projectionError is null)
                {
                    if (activeXids.Contains(member.Xid))
                    {
                        projectionMembers.Add(member);
                    }
                    continue;
                }
                bool ownsResolutionDefinitions = emitsDeclarations &&
                    activeXids.Contains(member.Xid) && !declarationOwnerAssigned;
                declarationOwnerAssigned |= ownsResolutionDefinitions;

                (UANodeSet? nodeSet, ExpandedNodeId root, string? conversionError, WoTPhaseEnum failurePhase,
                    ArrayOf<WotProjectedAffordance> affordances) =
                    projectionError is not null
                        ? (null, default, projectionError, WoTPhaseEnum.FormatValidation, [])
                        : await TryConvertAsync(
                            member, snapshot, contentCache, new WotDocumentConversionContents(
                                contentCache, capture.Inputs.DeclarationInputs, member, conversionMembers,
                                ownsResolutionDefinitions), cancellationToken)
                            .ConfigureAwait(false);
                if (nodeSet is not null && m_nodeSetContributors.Length > 0)
                {
                    // Contributors run after conversion and before any variable is created, which
                    // is when a programmatically discovered DataType (a controller UDT, say) has to
                    // exist for a uav:mapByFieldPath mapping to resolve against it.
                    foreach (IWotNodeSetContributor contributor in m_nodeSetContributors)
                    {
                        await contributor
                            .ContributeAsync(member, nodeSet, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                if (nodeSet is null)
                {
                    foreach (WotResource affected in activeXids.Contains(member.Xid) ? [member] : activeMembers)
                    {
                        results.Add(FailResult(
                            affected,
                            generation,
                            failurePhase,
                            conversionError));
                        if (failurePhase is not (WoTPhaseEnum.FormatValidation or WoTPhaseEnum.CompatibilityValidation))
                        {
                            projections.Add(FailProjection(affected, conversionError));
                            RaiseLoadFailure(affected, generation, failurePhase, conversionError);
                        }
                        else
                        {
                            WoTValidationOutcomeDataType validation =
                                ValidationFailure(conversionError, failurePhase);
                            projections.Add(FailProjection(
                                affected,
                                conversionError,
                                validation));
                            RaiseValidationFailure(
                                affected,
                                generation,
                                failurePhase,
                                validation,
                                conversionError);
                        }
                    }
                    return new ClosureOutcome(
                        results.ToImmutable(),
                        projections,
                        retiredMembers);
                }

                declarationContext.AddNodeSet(member.Xid, nodeSet, root, memberContent);
                if (activeXids.Contains(member.Xid))
                {
                    WotBindingPlanRequest planRequest = (await BuildPlanRequestAsync(
                        member, version, memberContent, snapshot, contentCache, cancellationToken)
                        .ConfigureAwait(false))
                        .WithProjectionRoot(root);
                    if (!affordances.IsEmpty)
                    {
                        planRequest = planRequest.WithProjectedAffordances(affordances);
                    }
                    preparedPlans.Add((member, planRequest));
                }

                perMemberNodeCount[member.Xid] = nodeSet.Items?.Length ?? 0;
                if (!root.IsNull)
                {
                    perMemberRoot[member.Xid] = root;
                    if (activeXids.Contains(member.Xid) && ServerNamespaceUris is not null &&
                        m_preparing is { } preparing)
                    {
                        preparing.SourceRoots[member.Xid] = root;
                    }
                }
                if (activeXids.Contains(member.Xid))
                {
                    if (m_preparing is not null && WotConNamespaceIndex() != 0 && !root.IsNull)
                    {
                        AddPreparedSourceCorrelation(nodeSet, root, member);
                    }
                    convertedSources.Add((member.ResourceId, nodeSet, memberContent));
                    CollectRequiredNamespaces(nodeSet, requiredNamespaces, ownedNamespaces);
                }
            }

            try
            {
                sources.AddRange(CoalesceProjectionSources(convertedSources, cancellationToken));
            }
            catch (ServiceResultException exception)
            {
                foreach (WotResource member in activeMembers)
                {
                    results.Add(FailResult(member, generation, WoTPhaseEnum.Projection, exception.Message));
                    projections.Add(FailProjection(member, exception.Message));
                    RaiseLoadFailure(member, generation, WoTPhaseEnum.Projection, exception.Message);
                }
                return new ClosureOutcome(
                    results.ToImmutable(),
                    projections,
                    retiredMembers);
            }

            // Resolve any companion-specification namespace the closure depends on that neither the
            // closure itself nor the server already provides. Resolved models are prepended to the
            // sources so they materialize before the documents that reference them. A namespace
            // that stays unresolved is reported, never silently dropped: the projection then fails
            // with a message naming exactly what is missing.
            (ImmutableArray<WotProjectionSource> resolved, ImmutableArray<string> unresolved) =
                await ResolveDependencyModelsAsync(
                        requiredNamespaces, ownedNamespaces, declarationContext, cancellationToken)
                    .ConfigureAwait(false);
            if (!resolved.IsDefaultOrEmpty)
            {
                sources.InsertRange(0, resolved);
            }
            if (!unresolved.IsDefaultOrEmpty)
            {
                string reason = "Unresolved dependency namespaces: " + string.Join(", ", unresolved);
                foreach (WotResource member in activeMembers)
                {
                    results.Add(FailResult(member, generation, WoTPhaseEnum.DependencyResolution, reason));
                    projections.Add(FailProjection(member, reason));
                    RaiseLoadFailure(member, generation, WoTPhaseEnum.DependencyResolution, reason);
                }
                return new ClosureOutcome(results.ToImmutable(), projections, 0);
            }

            // All converted partitions and resolved models are available before
            // classifying any root, including ownership across partition order.
            foreach ((WotResource member, WotBindingPlanRequest request) in preparedPlans)
            {
                bool isDeclaration = await declarationContext.IsDeclarationAsync(member, cancellationToken)
                    .ConfigureAwait(false);
                WotBindingPlan plan = m_binders.Prepare(request.WithDeclarationContext(isDeclaration));
                bindingPlans.Add(plan);
                if (!plan.FullySupported)
                {
                    if (strict)
                    {
                        const string reason = "Unsupported binding forms in a strict closure.";
                        results.Add(FailResult(member, generation, WoTPhaseEnum.Projection, reason));
                        projections.Add(FailProjection(member, reason));
                        RaiseBindingFailure(member, generation, reason);
                        return new ClosureOutcome(
                            results.ToImmutable(),
                            projections,
                            retiredMembers);
                    }
                    degraded = true;
                    string bindingReason = member.ResourceId +
                        ": unsupported forms for " +
                        string.Join(
                            ", ", plan.UnsupportedForms.Select(form => form.AffordanceName).Distinct().Take(3));
                    degradationReasons.Add(bindingReason);
                    RaiseBindingFailure(member, generation, bindingReason);
                }
                else if (!isDeclaration && plan.HasNonExecutableForms)
                {
                    degraded = true;
                    degradationReasons.Add(member.ResourceId +
                        ": no executor for " +
                        string.Join(
                            ", ", plan.CompiledForms.Where(form => !form.IsExecutable)
                                .Select(form => form.AffordanceName).Distinct().Take(3)));
                }
            }

            if (dryRun)
            {
                foreach (WotResource member in activeMembers)
                {
                    results.Add(new WoTResourceLoadResultDataType
                    {
                        Xid = member.Xid,
                        GroupId = member.GroupId,
                        ResourceId = member.ResourceId,
                        VersionId = member.DefaultVersionId ?? string.Empty,
                        Kind = member.Kind,
                        Outcome = degraded ? WoTOutcomeEnum.Warning : WoTOutcomeEnum.Success,
                        Phase = WoTPhaseEnum.Projection,
                        LoadState = member.LoadState,
                        Generation = generation,
                        MaterializedNodeCount = (uint)(perMemberNodeCount.TryGetValue(
                            member.Xid, out int c) ? c : 0),
                        ContentDigest = DigestOf(member),
                        Message = "Dry run; no projection committed. Candidate generation " +
                            generation.ToString(CultureInfo.InvariantCulture) +
                            "."
                    });
                }
                return new ClosureOutcome(
                    results.ToImmutable(),
                    projections,
                    retiredMembers);
            }

            var document = new WotProjectionDocument(
                closure.Key, sources.ToImmutable(), bindingPlans.ToArrayOf());
            WotProjectionHandle? handle = tracked?.Handle;
            if (sources.Count > 0)
            {
                try
                {
                    if (tracked?.Handle is null)
                    {
                        handle = await m_host.AddAsync(document, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else if (capture.RetirementPolicy == WotProjectionRetirementPolicy.Immediate)
                    {
                        handle = await m_host.ImmediateReloadAsync(
                            tracked.Handle, document, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        handle = await m_host.ShadowReloadAsync(
                            tracked.Handle, document, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Projection failed: retain the previous active generation and its
                    // active-member binding plans. Inactive dependency-only members
                    // were retired before the attempt and must not be marked failed.
                    foreach (WotResource member in activeMembers)
                    {
                        results.Add(FailResult(
                            member, generation, WoTPhaseEnum.Activation, ex.Message));
                        projections.Add(FailProjection(member, ex.Message));
                        RaiseLoadFailure(member, generation, WoTPhaseEnum.Activation, ex.Message);
                    }
                    return new ClosureOutcome(
                        results.ToImmutable(),
                        projections,
                        retiredMembers,
                        ByteString.From(aggregateDigest));
                }
            }

            string projectionWarning = handle?.Warning ?? string.Empty;
            if (projectionWarning.Length != 0)
            {
                degraded = true;
            }

            // Materialize a View for each deferred projection-document member now
            // that its sources are in the address space. The View Organizes the
            // already-materialized source Nodes (Section 12.6) and creates no
            // affordance Node of its own.
            var projectionXids = new HashSet<string>(StringComparer.Ordinal);
            var viewResults = new List<WoTResourceLoadResultDataType>();
            var viewProjections = new List<WotResourceProjection>();
            var viewHandles = new List<WotViewProjectionHandle>();
            if (projectionMembers.Count > 0)
            {
                await MaterializeProjectionViewsAsync(
                    snapshot, projectionMembers, perMemberRoot, declarationContext, generation,
                    projectionXids, viewResults, viewProjections, viewHandles,
                    contentCache, cancellationToken).ConfigureAwait(false);
            }

            // The shadow switch (or first add) succeeded. On an update, retire the
            // previously tracked binding plans before publishing the new closure
            // state so they are not leaked. This runs after the successful switch
            // and before the closure state is replaced; deactivating the old plans
            // first (then activating the new plans below) keeps a resource that is
            // shared between the old and new plan sets continuously bound.
            if (tracked is not null)
            {
                foreach (WotBindingPlan plan in tracked.BindingPlans)
                {
                    await m_binders.DeactivateAsync(plan, cancellationToken).ConfigureAwait(false);
                }
                foreach (WotViewProjectionHandle viewHandle in tracked.ViewHandles)
                {
                    await m_viewHost.RemoveAsync(viewHandle, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            ClosureStates[closure.Key] = new ClosureState
            {
                Key = closure.Key,
                Handle = handle,
                AggregateDigest = aggregateDigest,
                Generation = generation,
                MemberXids = [.. members.Select(m => m.Xid)],
                Members = [.. activeMembers.Select(m => new ClosureMemberState
                {
                    Xid = m.Xid,
                    GroupId = m.GroupId,
                    ResourceId = m.ResourceId,
                    VersionId = m.DefaultVersionId ?? string.Empty,
                    Kind = m.Kind,
                    ContentDigest = DigestOf(m)
                })],
                ModelNamespaceUris = [.. sources.SelectMany(s => s.ModelNamespaceUris)],
                BindingPlans = [.. bindingPlans],
                ViewHandles = [.. viewHandles]
            };
            foreach (string namespaceUri in sources.SelectMany(s => s.ModelNamespaceUris))
            {
                ProjectionNamespaces.Add(namespaceUri);
            }

            foreach (WotBindingPlan plan in bindingPlans)
            {
                await m_binders.ActivateAsync(plan, cancellationToken).ConfigureAwait(false);
            }

            WoTOutcomeEnum memberOutcome = degraded ? WoTOutcomeEnum.Warning : WoTOutcomeEnum.Success;
            foreach (WotResource member in activeMembers)
            {
                if (projectionXids.Contains(member.Xid))
                {
                    // A projection-document member is reported by the View
                    // materialization below, not as a NodeSet projection.
                    continue;
                }
                int nodeCount = perMemberNodeCount.TryGetValue(member.Xid, out int c) ? c : 0;
                NodeId rootNodeId = perMemberRoot.TryGetValue(member.Xid, out ExpandedNodeId root)
                    ? ResolveRootNodeId(root)
                    : NodeId.Null;
                WoTValidationOutcomeDataType validation = SuccessValidation();
                results.Add(new WoTResourceLoadResultDataType
                {
                    Xid = member.Xid,
                    GroupId = member.GroupId,
                    ResourceId = member.ResourceId,
                    VersionId = member.DefaultVersionId ?? string.Empty,
                    Kind = member.Kind,
                    Outcome = memberOutcome,
                    Phase = WoTPhaseEnum.Activation,
                    LoadState = WoTLoadStateEnum.Active,
                    Generation = generation,
                    MaterializedNodeCount = (uint)nodeCount,
                    RootNodeId = rootNodeId,
                    ContentDigest = DigestOf(member),
                    Message = projectionWarning.Length != 0
                        ? "Projected with warning: " + projectionWarning
                        : degraded ? DescribeDegradedBindings(degradationReasons) : "Projected."
                });
                projections.Add(new WotResourceProjection(
                    member.GroupId,
                    member.ResourceId,
                    WoTLoadStateEnum.Active,
                    member.DefaultVersionId,
                    generation,
                    nodeCount,
                    rootNodeId,
                    validation,
                    projectionWarning.Length == 0
                        ? []
                        : [projectionWarning],
                    DateTime.UtcNow)
                {
                    VersionId = member.DefaultVersionId
                });
                RaiseResource(member, generation, memberOutcome, WoTLoadStateEnum.Active);
            }

            results.AddRange(viewResults);
            projections.AddRange(viewProjections);
            ClosureStates[closure.Key].PublishedMetadata = [.. projections];

            return new ClosureOutcome(
                results.ToImmutable(),
                projections,
                retiredMembers,
                ByteString.From(aggregateDigest));
        }

        private async ValueTask<(int Retired, ImmutableArray<WoTResourceLoadResultDataType> Results)>
            ReconcileRetirementsAsync(
            HashSet<string> targetKeys,
            uint generation,
            bool dryRun,
            CancellationToken cancellationToken)
        {
            ImmutableArray<WoTResourceLoadResultDataType>.Builder results =
                ImmutableArray.CreateBuilder<WoTResourceLoadResultDataType>();
            int retired = 0;
            foreach (string key in (List<string>)[.. ClosureStates.Keys.Where(k => !targetKeys.Contains(k))])
            {
                if (ClosureStates.TryGetValue(key, out ClosureState? state))
                {
                    if (state.Handle is not null)
                    {
                        retired++;
                        foreach (ClosureMemberState member in state.Members)
                        {
                            results.Add(RetiredResult(member, generation));
                        }
                        if (dryRun)
                        {
                            continue;
                        }
                        // Deactivate bindings before retiring the projection.
                        foreach (WotBindingPlan plan in state.BindingPlans)
                        {
                            await m_binders.DeactivateAsync(plan, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        await m_host.RemoveAsync(state.Handle, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    if (!dryRun)
                    {
                        foreach (WotViewProjectionHandle viewHandle in state.ViewHandles)
                        {
                            await m_viewHost.RemoveAsync(viewHandle, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        ClosureStates.Remove(key);
                    }
                }
            }
            return (retired, dryRun ? results.ToImmutable() : []);
        }

        private async ValueTask<(
            UANodeSet? NodeSet,
            ExpandedNodeId Root,
            string? Error,
            WoTPhaseEnum FailurePhase,
            ArrayOf<WotProjectedAffordance> Affordances)> TryConvertAsync(
            WotResource resource,
            WotRegistrySnapshot snapshot,
            Dictionary<string, ByteString> contentCache,
            IReadOnlyDictionary<string, ByteString> conversionContents,
            CancellationToken cancellationToken)
        {
            WotResourceVersion? version = resource.DefaultVersion;
            if (version is null)
            {
                return (
                    null,
                    default,
                    "Resource has no default version.",
                    WoTPhaseEnum.FormatValidation,
                    []);
            }
            ByteString content = await ReadCachedContentAsync(contentCache, version, cancellationToken)
                .ConfigureAwait(false);
            WotConversionOutput output = await m_converter.ConvertAsync(
                resource, content, snapshot, conversionContents, cancellationToken).ConfigureAwait(false);
            if (!output.Succeeded)
            {
                return (null, default, output.Errors.IsDefaultOrEmpty
                    ? "The document could not be converted to a NodeSet."
                    : string.Join("; ", output.Errors), output.FailurePhase, []);
            }
            return (output.NodeSet, output.RootNodeId, null, WoTPhaseEnum.Projection, output.ProjectedAffordances);
        }

        /// <summary>
        /// Resolves a projection root, recorded before lifecycle add as an
        /// absolute <see cref="ExpandedNodeId"/>, into a concrete server NodeId
        /// once its owning namespace has been registered by the projection host.
        /// Returns <c>NodeId.Null</c> when there is no root or the namespace table is
        /// unavailable or does not yet contain the owning namespace.
        /// </summary>
        private NodeId ResolveRootNodeId(ExpandedNodeId root)
        {
            if (root.IsNull)
            {
                return NodeId.Null;
            }
            NamespaceTable? namespaces = ServerNamespaceUris;
            if (namespaces is null)
            {
                return NodeId.Null;
            }
            var resolved = ExpandedNodeId.ToNodeId(root, namespaces);
            return resolved.IsNull ? NodeId.Null : resolved;
        }

        /// <summary>
        /// Classifies a stored plan and rechecks its admission before publishing
        /// any part of its materialization closure.
        /// </summary>
        private (bool IsProjection, string? Error, bool EmitsDeclarations) GetProjectionAdmission(
            WotResource resource, WotResourceVersion version, ByteString content)
        {
            using WotDocument? document = TryParseDocument(content);
            if (document is null)
            {
                return (false, WotProjectionAdmission.UsesProjectionFormat(version.Format, version.ContentType)
                    ? "The stored projection plan is not a well-formed JSON document within the configured bounds."
                    : null, false);
            }
            return (WotProjection.IsProjection(document), WotProjectionAdmission.GetError(
                document, resource.Kind, version.Format, version.ContentType, m_converterOptions.ProjectionCompatibilityMode),
                !WotNodeSetConverter.TakesRestorePath(document));
        }

        private WotDocument? TryParseDocument(ByteString content)
        {
            try
            {
                return WotDocument.Parse(content.Span.ToArray(), m_converterOptions);
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                return null;
            }
        }

        private static ValueTask<ByteString> ReadCachedContentAsync(
            Dictionary<string, ByteString> contentCache,
            WotResourceVersion version,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (contentCache.TryGetValue(version.DigestHex, out ByteString content))
            {
                return new ValueTask<ByteString>(content);
            }
            throw new ServiceResultException(
                StatusCodes.BadNotFound, "The exact Version was not acquired by this dependency snapshot.");
        }

        /// <summary>
        /// Materializes a View for every deferred projection-document member of a
        /// closure. Each View <c>Organizes</c> the Nodes already materialized from
        /// the projection's sources (located through the closure's per-member
        /// roots) and never recreates them; a source not present in this address
        /// space is omitted and reported, and the resource still reaches
        /// <c>Active</c> (WoT Binding Section 12.6).
        /// </summary>
        private async ValueTask MaterializeProjectionViewsAsync(
            WotRegistrySnapshot snapshot,
            List<WotResource> projectionMembers,
            Dictionary<string, ExpandedNodeId> perMemberRoot,
            WotProjectionDeclarationContext declarationContext,
            uint generation,
            HashSet<string> projectionXids,
            List<WoTResourceLoadResultDataType> viewResults,
            List<WotResourceProjection> viewProjections,
            List<WotViewProjectionHandle> viewHandles,
            Dictionary<string, ByteString> contentCache,
            CancellationToken cancellationToken)
        {
            // Map each already-materialized source resource to its server root
            // NodeId so the View can Organize the exact Nodes. A source absent
            // from this map is treated as not in this address space.
            NamespaceTable namespaces = ServerNamespaceUris ?? new NamespaceTable();
            if (m_preparing is not null)
            {
                var capturedNamespaces = new NamespaceTable();
                for (int i = 0; i < namespaces.Count; i++)
                {
                    capturedNamespaces.GetIndexOrAppend(namespaces.GetString((uint)i)
                        ?? throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid, "A source namespace is missing."));
                }
                namespaces = capturedNamespaces;
                foreach (KeyValuePair<string, ExpandedNodeId> entry in perMemberRoot)
                {
                    ExpandedNodeId root = entry.Value;
                    if (!string.IsNullOrEmpty(root.NamespaceUri))
                    {
                        namespaces.GetIndexOrAppend(root.NamespaceUri);
                    }
                    foreach (ExpandedNodeId nodeId in declarationContext.GetNodes(entry.Key))
                    {
                        if (!string.IsNullOrEmpty(nodeId.NamespaceUri))
                        {
                            namespaces.GetIndexOrAppend(nodeId.NamespaceUri);
                        }
                    }
                }
            }
            var sourceRoots = new Dictionary<string, NodeId>(StringComparer.Ordinal);
            foreach (WotResource resource in snapshot.AllResources())
            {
                if (!resource.RootNodeId.IsNull && resource.ActiveVersionId == resource.DefaultVersionId)
                {
                    sourceRoots[resource.Xid] = resource.RootNodeId;
                }
            }
            foreach (KeyValuePair<string, ExpandedNodeId> entry in perMemberRoot)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(entry.Value, namespaces);
                if (!nodeId.IsNull)
                {
                    sourceRoots[entry.Key] = nodeId;
                }
            }

            var index = new WotMaterializedNodeIndex(
                snapshot, namespaces, sourceRoots,
                (xid, nodeId) => declarationContext.ContainsNode(xid, nodeId.NamespaceIndex == 0
                    ? new ExpandedNodeId(nodeId)
                    : NodeId.ToExpandedNodeId(nodeId, namespaces)));
            var thingResolver = new SnapshotThingResolver(snapshot, contentCache);
            var builder = new WotProjectionViewBuilder(
                thingResolver, index, m_converterOptions, namespaces);

            foreach (WotResource member in projectionMembers)
            {
                projectionXids.Add(member.Xid);
                WotResourceVersion? version = member.DefaultVersion;
                if (version is null)
                {
                    continue;
                }

                try
                {
                    await MaterializeProjectionViewAsync(
                        builder, snapshot, namespaces, member, version, generation, contentCache,
                        viewResults, viewProjections, viewHandles, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Contain the failure to this member. Letting it escape would
                    // abort the loop with the Views applied so far held only in the
                    // local list, which the caller publishes into ClosureState only
                    // on the success path - so those Nodes would stay in the address
                    // space with no handle recorded and nothing could ever remove
                    // them. Reporting the member failed keeps every applied handle
                    // reachable for the next refresh to retire.
                    string reason = "The projection View could not be materialized: " +
                        ex.Message;
                    viewResults.Add(FailResult(
                        member, generation, WoTPhaseEnum.Activation, reason));
                    viewProjections.Add(FailProjection(member, reason));
                    RaiseLoadFailure(member, generation, WoTPhaseEnum.Activation, reason);
                }
            }
        }

        /// <summary>
        /// Materializes the View for one projection-document member. Expected
        /// failures are reported through the result lists; anything unexpected is
        /// contained by the caller.
        /// </summary>
        private async ValueTask MaterializeProjectionViewAsync(
            WotProjectionViewBuilder builder,
            WotRegistrySnapshot snapshot,
            NamespaceTable namespaces,
            WotResource member,
            WotResourceVersion version,
            uint generation,
            Dictionary<string, ByteString> contentCache,
            List<WoTResourceLoadResultDataType> viewResults,
            List<WotResourceProjection> viewProjections,
            List<WotViewProjectionHandle> viewHandles,
            CancellationToken cancellationToken)
        {
            ByteString content = await ReadCachedContentAsync(contentCache, version, cancellationToken)
                .ConfigureAwait(false);
            WotDocument? document = TryParseDocument(content);
            if (document is null)
            {
                const string reason = "The projection document could not be parsed.";
                viewResults.Add(FailResult(
                    member, generation, WoTPhaseEnum.FormatValidation, reason));
                viewProjections.Add(FailProjection(member, reason, ValidationFailure(reason)));
                RaiseValidationFailure(
                    member, generation, WoTPhaseEnum.FormatValidation, ValidationFailure(reason), reason);
                return;
            }

            WotViewProjectionResult build;
            NodeId viewNodeId;
            using (document)
            {
                viewNodeId = ComputeViewNodeId(member, document);
                build = m_preparing is null
                    ? await builder.BuildAsync(document, null, cancellationToken).ConfigureAwait(false)
                    : await builder.BuildCanonicalAsync(document, snapshot, cancellationToken).ConfigureAwait(false);
            }

            if (!build.Success || build.Plan is null)
            {
                string reason = FormatDiagnostics(build.Diagnostics,
                    "The projection document could not be materialized as a View.");
                viewResults.Add(FailResult(member, generation, WoTPhaseEnum.Projection, reason));
                viewProjections.Add(FailProjection(member, reason));
                RaiseLoadFailure(member, generation, WoTPhaseEnum.Projection, reason);
                return;
            }

            WotViewProjectionPlan plan = build.Plan;
            var request = new WotViewProjectionRequest(
                member.Xid, member.Xid, ComputeResourceNodeId(member), viewNodeId, plan)
            {
                CapturedNamespaceUris = Enumerable.Range(0, namespaces.Count)
                    .Select(index => namespaces.GetString((uint)index)
                        ?? throw new ServiceResultException(
                            StatusCodes.BadNodeIdInvalid, "A source namespace is missing."))
                    .ToArrayOf()
            };
            WotViewProjectionHandle viewHandle = await m_viewHost
                .ApplyAsync(request, cancellationToken)
                .ConfigureAwait(false);
            viewHandles.Add(viewHandle);

            WoTOutcomeEnum outcome =
                plan.Omissions.Count == 0 && viewHandle.Omissions.Count == 0
                ? WoTOutcomeEnum.Success
                : WoTOutcomeEnum.Warning;
            string message = FormatProjectionViewMessage(plan, viewHandle);
            viewResults.Add(new WoTResourceLoadResultDataType
            {
                Xid = member.Xid,
                GroupId = member.GroupId,
                ResourceId = member.ResourceId,
                VersionId = member.DefaultVersionId ?? string.Empty,
                Kind = member.Kind,
                Outcome = outcome,
                Phase = WoTPhaseEnum.Activation,
                LoadState = WoTLoadStateEnum.Active,
                Generation = generation,
                MaterializedNodeCount = (uint)plan.MaterializedNodeCount,
                RootNodeId = viewNodeId,
                ContentDigest = DigestOf(member),
                Message = message
            });
            viewProjections.Add(new WotResourceProjection(
                member.GroupId,
                member.ResourceId,
                WoTLoadStateEnum.Active,
                member.DefaultVersionId,
                generation,
                plan.MaterializedNodeCount,
                viewNodeId,
                SuccessValidation(),
                OmissionDiagnostics(plan.Omissions),
                DateTime.UtcNow)
            {
                VersionId = member.DefaultVersionId
            });
            RaiseResource(member, generation, outcome, WoTLoadStateEnum.Active);
        }

        private NodeId ComputeResourceNodeId(WotResource member)
        {
            return new NodeId(
                $"WoTRegistry/groups/{member.GroupId}/resources/{member.ResourceId}",
                WotConNamespaceIndex());
        }

        private NodeId ComputeViewNodeId(WotResource member, WotDocument document)
        {
            if (document.TryGetUav("id", out JsonElement identity))
            {
                if (identity.ValueKind != JsonValueKind.String ||
                    !WotPortableIdentity.IsPortableNodeId(identity.GetString()))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "A canonical View requires a portable authored NodeId.");
                }
                ExpandedNodeId authored = ExpandedNodeId.Parse(identity.GetString()!);
                NamespaceTable namespaces = ServerNamespaceUris ?? new NamespaceTable();
                if (!string.IsNullOrEmpty(authored.NamespaceUri) &&
                    namespaces.GetIndex(authored.NamespaceUri) < 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid,
                        "The authored View namespace is not present in the current source image.");
                }
                NodeId nodeId = ExpandedNodeId.ToNodeId(authored, namespaces);
                if (nodeId.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdInvalid, "A canonical View cannot have a null NodeId.");
                }
                return nodeId;
            }
            return new NodeId(
                $"WoTRegistry/groups/{member.GroupId}/resources/{member.ResourceId}/View",
                WotConNamespaceIndex());
        }

        private ushort WotConNamespaceIndex()
        {
            NamespaceTable? namespaces = ServerNamespaceUris;
            if (namespaces is null)
            {
                return 0;
            }
            int index = namespaces.GetIndex(Namespaces.WotCon);
            return index > 0 ? (ushort)index : (ushort)0;
        }

        private static ImmutableArray<string> OmissionDiagnostics(ArrayOf<string> omissions)
        {
            if (omissions.Count == 0)
            {
                return [];
            }
            ImmutableArray<string>.Builder builder =
                ImmutableArray.CreateBuilder<string>(omissions.Count);
            for (int i = 0; i < omissions.Count; i++)
            {
                builder.Add(omissions[i]);
            }
            return builder.ToImmutable();
        }

        private static string FormatProjectionViewMessage(
            WotViewProjectionPlan plan,
            WotViewProjectionHandle viewHandle)
        {
            int selectedCount = CountOrganizedMembers(plan) + plan.Omissions.Count;
            // The host reports the plan's own omissions back plus any member it
            // could not organize, so its count is the authoritative one. A host
            // that reports none (a test double) still honours the plan's.
            int omittedCount = Math.Max(viewHandle.Omissions.Count, plan.Omissions.Count);
            int organizedCount = selectedCount - omittedCount;
            if (omittedCount == 0)
            {
                return viewHandle.Message.Length != 0
                    ? viewHandle.Message
                    : "Materialized projection View organizing " +
                        organizedCount.ToString(CultureInfo.InvariantCulture) +
                        " Node(s).";
            }

            string summary = organizedCount == 0
                ? "Materialized projection View organizing 0 Node(s); omitted all " +
                    selectedCount.ToString(CultureInfo.InvariantCulture) +
                    " selected member(s)."
                : "Materialized projection View organizing " +
                    organizedCount.ToString(CultureInfo.InvariantCulture) +
                    " of " +
                    selectedCount.ToString(CultureInfo.InvariantCulture) +
                    " selected member(s); omitted " +
                    omittedCount.ToString(CultureInfo.InvariantCulture) +
                    ".";
            return viewHandle.Message.Length == 0 ? summary : summary + " " + viewHandle.Message;
        }

        private static int CountOrganizedMembers(WotViewProjectionPlan plan)
        {
            return plan.OrganizedNodeIds.Count + CountOrganizedMembers(plan.Groups);
        }

        private static int CountOrganizedMembers(ArrayOf<WotOrganizationalGroup> groups)
        {
            int count = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                count += groups[i].OrganizedNodeIds.Count + CountOrganizedMembers(groups[i].Groups);
            }
            return count;
        }

        private static string FormatDiagnostics(ArrayOf<WotDiagnostic> diagnostics, string fallback)
        {
            if (diagnostics.Count == 0)
            {
                return fallback;
            }
            var parts = new List<string>(diagnostics.Count);
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Severity == WotDiagnosticSeverity.Error)
                {
                    parts.Add(diagnostics[i].Message);
                }
            }
            return parts.Count == 0 ? fallback : string.Join("; ", parts);
        }

        /// <summary>
        /// Builds the binding plan request for one closure member, resolving
        /// the EventType definitions its event affordances link to with
        /// <c>tm:ref</c> before the synchronous planning that consumes them
        /// (WoT Binding Section 6.1).
        /// </summary>
        /// <remarks>
        /// The links are resolved against the closure itself: the same
        /// <see cref="SnapshotThingResolver"/> and content cache the conversion
        /// uses hold the sibling documents, and the dependency graph already
        /// treats a <c>tm:ref</c> as an edge, so an EventType Thing Model is a
        /// member of the closure and is loaded with it. Nothing is fetched over
        /// the network, and planning itself stays synchronous and side-effect
        /// free. A link that does not resolve leaves the affordance out of the
        /// catalog, which the planner reports as an unsupported form: the
        /// closure then fails strictly or materializes degraded, and the
        /// failure names the affordance rather than being swallowed here.
        /// </remarks>
        private async ValueTask<WotBindingPlanRequest> BuildPlanRequestAsync(
            WotResource resource,
            WotResourceVersion version,
            ByteString content,
            WotRegistrySnapshot snapshot,
            IReadOnlyDictionary<string, ByteString> contentCache,
            CancellationToken cancellationToken)
        {
            byte[] utf8 = content.Span.ToArray();
            var thingResolver = new SnapshotThingResolver(snapshot, contentCache);
            WotEventSelectionCatalog catalog = await WotBindingPlanRequest
                .ResolveEventSelectionsAsync(
                    utf8,
                    thingResolver,
                    m_converterOptions.MaxJsonDepth,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            return WotBindingPlanRequest.FromDocument(
                resource.Xid, resource.Kind, utf8, catalog, m_converterOptions.MaxJsonDepth);
        }

        private string BinderVersion
        {
            get
            {
                IReadOnlyList<WoTBindingCapabilityDataType> caps = m_binders.Capabilities;
                if (caps.Count == 0)
                {
                    return "none";
                }
                var builder = new StringBuilder();
                foreach (WoTBindingCapabilityDataType cap in caps)
                {
                    builder.Append(cap.BindingUri).Append(';').Append(cap.ProfileVersion).Append('|');
                }
                return builder.ToString();
            }
        }

        private static byte[] SerializeNodeSet(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }

        private static string DescribeDegradedBindings(List<string> reasons)
        {
            string detail = string.Join("; ", reasons.Take(3));
            if (detail.Length > 1024)
            {
                detail = detail[..1024] + "...";
            }
            return "Projected with degraded bindings: " +
                detail +
                (reasons.Count > 3 ? "; see BindingFailure events for additional resources." : string.Empty);
        }

        private ImmutableArray<WotProjectionSource> CoalesceProjectionSources(
            List<(string Name, UANodeSet Nodes, ByteString Content)> converted,
            CancellationToken cancellationToken)
        {
            ImmutableArray<WotProjectionSource>.Builder result = ImmutableArray.CreateBuilder<WotProjectionSource>();
            foreach (IGrouping<string, (string Name, UANodeSet Nodes, ByteString Content)> group in
                converted.GroupBy(source => string.Join(
                    "\u001f", OwnedModelUris(source.Nodes).OrderBy(uri => uri, StringComparer.Ordinal))))
            {
                var partitions = group.ToList();
                (string name, UANodeSet first, _) = partitions[0];
                if (partitions.Count == 1 &&
                    m_converterOptions.DocumentSetMode == WotDocumentSetMode.PartitionReconstruction)
                {
                    result.Add(new WotProjectionSource(name, OwnedModelUris(first), SerializeNodeSet(first)));
                    continue;
                }
                var entries = new List<WotDocumentSetEntry>();
                try
                {
                    foreach ((string partitionName, UANodeSet _, ByteString partitionContent) in partitions)
                    {
                        entries.Add(new WotDocumentSetEntry(
                            partitionName, WotDocument.Parse(partitionContent.Memory, m_converterOptions)));
                    }
                    using var documents = new WotDocumentSet(name, entries.ToArrayOf());
                    entries.Clear();
                    WotConversionResult<UANodeSet> merged = WotNodeSetConverter.MergeNodeSetPartitions(
                        documents, partitions.Select(partition => partition.Nodes).ToArrayOf(),
                        m_converterOptions, cancellationToken);
                    if (!merged.Success || merged.Value is null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError,
                            "Linked model partitions conflict: " +
                            string.Join("; ", merged.Diagnostics.Select(diagnostic => diagnostic.Message)));
                    }
                    result.Add(new WotProjectionSource(
                        name, OwnedModelUris(merged.Value), SerializeNodeSet(merged.Value)));
                }
                finally
                {
                    foreach (WotDocumentSetEntry entry in entries)
                    {
                        entry.Dispose();
                    }
                }
            }
            return result.ToImmutable();
        }

        private static ImmutableArray<string> OwnedModelUris(UANodeSet nodeSet)
        {
            if (nodeSet.Models is { Length: > 0 })
            {
                var uris = new List<string>(nodeSet.Models.Length);
                foreach (ModelTableEntry model in nodeSet.Models)
                {
                    if (!string.IsNullOrEmpty(model.ModelUri))
                    {
                        uris.Add(model.ModelUri);
                    }
                }
                if (uris.Count > 0)
                {
                    return [.. uris];
                }
            }
            if (nodeSet.NamespaceUris is { Length: > 0 })
            {
                return
                [
                    .. nodeSet.NamespaceUris
                        .Where(u => !string.Equals(u, Ua.Namespaces.OpcUa, StringComparison.Ordinal))
                ];
            }
            return [];
        }

        /// <summary>
        /// Records the namespaces a converted NodeSet owns and the ones it declares a dependency
        /// on, so the closure's unmet dependencies can be resolved once for the whole projection.
        /// </summary>
        private static void CollectRequiredNamespaces(
            UANodeSet nodeSet,
            HashSet<string> required,
            HashSet<string> owned)
        {
            foreach (string uri in OwnedModelUris(nodeSet))
            {
                owned.Add(uri);
            }
            if (nodeSet.Models is null)
            {
                return;
            }
            foreach (ModelTableEntry model in nodeSet.Models)
            {
                if (model?.RequiredModel is null)
                {
                    continue;
                }
                foreach (ModelTableEntry dependency in model.RequiredModel)
                {
                    if (!string.IsNullOrEmpty(dependency?.ModelUri) &&
                        !string.Equals(
                            dependency!.ModelUri, Ua.Namespaces.OpcUa, StringComparison.Ordinal))
                    {
                        required.Add(dependency.ModelUri);
                    }
                }
            }
        }

        /// <summary>
        /// Asks the configured <see cref="IWotNodeSetResolver"/> for every dependency namespace the
        /// closure needs but neither owns nor finds on the server, recursing into whatever it gets
        /// back. Returns the resolved models in dependency order together with the namespaces that
        /// stayed unresolved.
        /// </summary>
        private async ValueTask<(ImmutableArray<WotProjectionSource> Resolved,
            ImmutableArray<string> Unresolved)> ResolveDependencyModelsAsync(
            HashSet<string> required,
            HashSet<string> owned,
            WotProjectionDeclarationContext declarationContext,
            CancellationToken cancellationToken)
        {
            var pending = new Queue<string>();
            foreach (string uri in required)
            {
                if (!owned.Contains(uri) &&
                    !await IsKnownToServerAsync(uri, cancellationToken).ConfigureAwait(false))
                {
                    pending.Enqueue(uri);
                }
            }
            if (pending.Count == 0)
            {
                return ([], []);
            }
            if (m_nodeSetResolver is null)
            {
                return ([], [.. pending]);
            }

            ImmutableArray<WotProjectionSource>.Builder resolved =
                ImmutableArray.CreateBuilder<WotProjectionSource>();
            ImmutableArray<string>.Builder unresolved = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>(pending, StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string uri = pending.Dequeue();
                Stream? stream = await m_nodeSetResolver
                    .TryResolveAsync(uri, cancellationToken)
                    .ConfigureAwait(false);
                if (stream is null)
                {
                    unresolved.Add(uri);
                    continue;
                }

                byte[] xml;
                UANodeSet? dependency;
                using (stream)
                {
                    MemoryStream? buffer = await CopyResolverDocumentAsync(
                            stream, uri, unresolved, cancellationToken)
                        .ConfigureAwait(false);
                    if (buffer is null)
                    {
                        continue;
                    }
                    using (buffer)
                    {
                        xml = buffer.ToArray();
                        buffer.Position = 0;
                        dependency = UANodeSet.Read(buffer);
                    }
                }
                if (dependency is null)
                {
                    // A resolver that hands back something unreadable is treated exactly like one
                    // that declined, so the namespace is reported rather than faulting onboarding.
                    unresolved.Add(uri);
                    continue;
                }

                declarationContext.AddDependencyNodeSet(dependency);
                foreach (string ownedUri in OwnedModelUris(dependency))
                {
                    owned.Add(ownedUri);
                }
                // A resolved model may itself depend on further namespaces.
                var nested = new HashSet<string>(StringComparer.Ordinal);
                CollectRequiredNamespaces(dependency, nested, owned);
                foreach (string nestedUri in nested)
                {
                    if (!owned.Contains(nestedUri) &&
                        !await IsKnownToServerAsync(nestedUri, cancellationToken).ConfigureAwait(false) &&
                        seen.Add(nestedUri))
                    {
                        pending.Enqueue(nestedUri);
                    }
                }
                resolved.Add(new WotProjectionSource(uri, OwnedModelUris(dependency), xml));
            }

            // Dependencies are appended in resolution order, so reverse to put the deepest model
            // first: a model must be materialized before the model that requires it.
            resolved.Reverse();
            return (resolved.ToImmutable(), unresolved.ToImmutable());
        }

        private async ValueTask<bool> IsKnownToServerAsync(string namespaceUri, CancellationToken cancellationToken)
        {
            foreach (ClosureState closure in ClosureStates.Values)
            {
                if (closure.Handle is not null &&
                    closure.ModelNamespaceUris.Contains(namespaceUri, StringComparer.Ordinal))
                {
                    return true;
                }
            }
            if (ProjectionNamespaces.Contains(namespaceUri))
            {
                return false;
            }
            return m_addressSpace is not null &&
                await m_addressSpace.HoldsNamespaceAsync(namespaceUri, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<MemoryStream?> CopyResolverDocumentAsync(
            Stream stream,
            string namespaceUri,
            ImmutableArray<string>.Builder unresolved,
            CancellationToken cancellationToken)
        {
            int maxBytes = m_converterOptions.MaxResolverDocumentBytes;
            var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            bool keepBuffer = false;
            try
            {
                while (true)
                {
                    int read = await ReadBlockAsync(stream, chunk, cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        buffer.Position = 0;
                        keepBuffer = true;
                        return buffer;
                    }
                    if (buffer.Length + read > maxBytes)
                    {
                        unresolved.Add(
                            namespaceUri +
                            " (resolver response exceeded " +
                            maxBytes.ToString(CultureInfo.InvariantCulture) +
                            " bytes)");
                        return null;
                    }
                    buffer.Write(chunk, 0, read);
                }
            }
            finally
            {
                if (!keepBuffer)
                {
                    buffer.Dispose();
                }
            }
        }

        private static async ValueTask<int> ReadBlockAsync(
            Stream stream,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
#if NETFRAMEWORK || NETSTANDARD2_0
            return await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                .ConfigureAwait(false);
#else
            return await stream.ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
#endif
        }

        private static IReadOnlyList<WotResource> MembersOf(WotDependencyClosure closure)
        {
            return closure.Members.IsDefaultOrEmpty
                ? Array.Empty<WotResource>()
                : closure.Members;
        }

        private async ValueTask RetireInactiveArtifactsAsync(
            ClosureState tracked,
            HashSet<string> activeXids,
            CancellationToken cancellationToken)
        {
            ImmutableArray<WotBindingPlan>.Builder retainedPlans =
                ImmutableArray.CreateBuilder<WotBindingPlan>();
            foreach (WotBindingPlan plan in tracked.BindingPlans)
            {
                if (activeXids.Contains(plan.ResourceXid))
                {
                    retainedPlans.Add(plan);
                }
                else
                {
                    await m_binders.DeactivateAsync(plan, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            tracked.BindingPlans = retainedPlans.ToImmutable();

            ImmutableArray<WotViewProjectionHandle>.Builder retainedViews =
                ImmutableArray.CreateBuilder<WotViewProjectionHandle>();
            foreach (WotViewProjectionHandle viewHandle in tracked.ViewHandles)
            {
                if (activeXids.Contains(viewHandle.ResourceXid))
                {
                    retainedViews.Add(viewHandle);
                }
                else
                {
                    await m_viewHost.RemoveAsync(viewHandle, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            tracked.ViewHandles = retainedViews.ToImmutable();
            tracked.Members =
            [
                .. tracked.Members.Where(member => activeXids.Contains(member.Xid))
            ];
        }

        private static bool ProjectionStateMatches(
            ClosureState tracked,
            IReadOnlyList<WotResource> activeMembers)
        {
            if (tracked.Members.Length != activeMembers.Count)
            {
                return false;
            }
            foreach (WotResource member in activeMembers)
            {
                ClosureMemberState? projected = tracked.Members.FirstOrDefault(candidate =>
                    string.Equals(candidate.Xid, member.Xid, StringComparison.Ordinal));
                WotResourceProjection? metadata = tracked.PublishedMetadata.FirstOrDefault(candidate =>
                    candidate.GroupId == member.GroupId && candidate.ResourceId == member.ResourceId);
                if (projected is null ||
                    metadata is null ||
                    member.LoadState != WoTLoadStateEnum.Active ||
                    member.RootNodeId != metadata.RootNodeId ||
                    member.MaterializedNodeCount != metadata.MaterializedNodeCount ||
                    member.RefreshGeneration != metadata.RefreshGeneration ||
                    !string.Equals(
                        member.ActiveVersionId,
                        projected.VersionId,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private HashSet<string> ResolveSelection(
            WotRegistrySnapshot snapshot,
            ImmutableArray<WoTResourceSelectorDataType> selectors)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (selectors.IsDefaultOrEmpty)
            {
                return set;
            }
            foreach (WoTResourceSelectorDataType selector in selectors)
            {
                foreach (WotResource resource in snapshot.AllResources())
                {
                    if (Matches(resource, selector))
                    {
                        set.Add(resource.Xid);
                    }
                }
            }
            return set;
        }

        private static bool Matches(WotResource resource, WoTResourceSelectorDataType selector)
        {
            if (!string.IsNullOrEmpty(selector.Xid) &&
                !string.Equals(selector.Xid, resource.Xid, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(selector.GroupId) &&
                !string.Equals(selector.GroupId, resource.GroupId, StringComparison.Ordinal))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(selector.ResourceId) &&
                !string.Equals(selector.ResourceId, resource.ResourceId, StringComparison.Ordinal))
            {
                return false;
            }
            return true;
        }

        private static WoTResourceLoadResultDataType FailResult(
            WotResource resource, uint generation, WoTPhaseEnum phase, string? message)
        {
            return new()
            {
                Xid = resource.Xid,
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId,
                VersionId = resource.DefaultVersionId ?? string.Empty,
                Kind = resource.Kind,
                Outcome = WoTOutcomeEnum.Failed,
                Phase = phase,
                LoadState = WoTLoadStateEnum.Failed,
                Generation = generation,
                MaterializedNodeCount = 0,
                ContentDigest = DigestOf(resource),
                Message = message ?? string.Empty
            };
        }

        private static WoTResourceLoadResultDataType UnchangedResult(
            WotResource resource, uint generation)
        {
            return new()
            {
                Xid = resource.Xid,
                GroupId = resource.GroupId,
                ResourceId = resource.ResourceId,
                VersionId = resource.ActiveVersionId ?? resource.DefaultVersionId ?? string.Empty,
                Kind = resource.Kind,
                Outcome = WoTOutcomeEnum.Unchanged,
                Phase = WoTPhaseEnum.Activation,
                LoadState = WoTLoadStateEnum.Active,
                Generation = generation,
                MaterializedNodeCount = (uint)resource.MaterializedNodeCount,
                ContentDigest = DigestOf(resource),
                Message = "Content digest unchanged."
            };
        }

        private static WoTResourceLoadResultDataType RetiredResult(
            ClosureMemberState member, uint generation)
        {
            return new()
            {
                Xid = member.Xid,
                GroupId = member.GroupId,
                ResourceId = member.ResourceId,
                VersionId = member.VersionId,
                Kind = member.Kind,
                Outcome = WoTOutcomeEnum.Skipped,
                Phase = WoTPhaseEnum.Activation,
                LoadState = WoTLoadStateEnum.Unloaded,
                Generation = generation,
                MaterializedNodeCount = 0,
                ContentDigest = member.ContentDigest,
                Message = "Dry run; projection would be retired at candidate generation " +
                    generation.ToString(CultureInfo.InvariantCulture) +
                    "."
            };
        }

        private static WotResourceProjection FailProjection(
            WotResource resource, string? message, WoTValidationOutcomeDataType? validation = null)
        {
            return new(
                        resource.GroupId,
                        resource.ResourceId,
                        WoTLoadStateEnum.Failed,
                        activeVersionId: null,
                        resource.RefreshGeneration,
                        resource.MaterializedNodeCount,
                        rootNodeId: NodeId.Null,
                        validation,
                        string.IsNullOrEmpty(message)
                            ? []
                            : [message!],
                        DateTime.UtcNow)
            {
                // Keep the previous active projection when a refresh fails.
                RetainPreviousActiveVersion = true,
                VersionId = resource.DefaultVersionId
            };
        }

        private static WoTValidationOutcomeDataType SuccessValidation()
        {
            return new()
            {
                FormatValidated = true,
                FormatOutcome = WoTOutcomeEnum.Success,
                CompatibilityValidated = true,
                CompatibilityOutcome = WoTOutcomeEnum.Success,
                ValidatedAt = DateTime.UtcNow,
                VocabularyVersion = WotNodeSetConverter.VocabularyNamespace
            };
        }

        private static WoTValidationOutcomeDataType ValidationFailure(
            string? reason, WoTPhaseEnum phase = WoTPhaseEnum.FormatValidation)
        {
            bool compatibility = phase == WoTPhaseEnum.CompatibilityValidation;
            return new()
            {
                FormatValidated = !compatibility,
                FormatOutcome = compatibility ? WoTOutcomeEnum.Skipped : WoTOutcomeEnum.Failed,
                FormatReason = compatibility ? string.Empty : reason ?? string.Empty,
                CompatibilityValidated = compatibility,
                CompatibilityOutcome = compatibility ? WoTOutcomeEnum.Failed : WoTOutcomeEnum.Skipped,
                CompatibilityReason = compatibility ? reason ?? string.Empty : string.Empty,
                ValidatedAt = DateTime.UtcNow,
                VocabularyVersion = WotNodeSetConverter.VocabularyNamespace
            };
        }

        private void RaiseResource(
            WotResource resource, uint generation, WoTOutcomeEnum outcome, WoTLoadStateEnum state)
        {
            RaiseEvent(new WotMaterializationEventArgs(WotMaterializationEventKind.Resource)
            {
                Xid = resource.Xid,
                ResourceId = resource.ResourceId,
                VersionId = resource.DefaultVersionId ?? string.Empty,
                DocumentKind = resource.Kind,
                Generation = generation,
                Phase = WoTPhaseEnum.Activation,
                Outcome = outcome,
                LoadState = state
            });
        }

        private void RaiseLoadFailure(
            WotResource resource, uint generation, WoTPhaseEnum phase, string? reason)
        {
            RaiseEvent(new WotMaterializationEventArgs(WotMaterializationEventKind.LoadFailure)
            {
                Xid = resource.Xid,
                ResourceId = resource.ResourceId,
                VersionId = resource.DefaultVersionId ?? string.Empty,
                DocumentKind = resource.Kind,
                Generation = generation,
                Phase = phase,
                Outcome = WoTOutcomeEnum.Failed,
                LoadState = WoTLoadStateEnum.Failed,
                Reason = reason ?? string.Empty
            });
        }

        private void RaiseValidationFailure(
            WotResource resource, uint generation, WoTPhaseEnum phase,
            WoTValidationOutcomeDataType validation, string? reason)
        {
            RaiseEvent(new WotMaterializationEventArgs(
                        WotMaterializationEventKind.ValidationFailure)
            {
                Xid = resource.Xid,
                ResourceId = resource.ResourceId,
                VersionId = resource.DefaultVersionId ?? string.Empty,
                DocumentKind = resource.Kind,
                Generation = generation,
                Phase = phase,
                Outcome = WoTOutcomeEnum.Failed,
                LoadState = WoTLoadStateEnum.Failed,
                Validation = validation,
                Reason = reason ?? string.Empty
            });
        }

        private void RaiseBindingFailure(WotResource resource, uint generation, string? reason)
        {
            RaiseEvent(new WotMaterializationEventArgs(
                        WotMaterializationEventKind.BindingFailure)
            {
                Xid = resource.Xid,
                ResourceId = resource.ResourceId,
                VersionId = resource.DefaultVersionId ?? string.Empty,
                DocumentKind = resource.Kind,
                Generation = generation,
                Phase = WoTPhaseEnum.Projection,
                Outcome = WoTOutcomeEnum.Failed,
                LoadState = WoTLoadStateEnum.Failed,
                Reason = reason ?? string.Empty
            });
        }

        private void RaiseEvent(WotMaterializationEventArgs args)
        {
            if (m_preparing is { } preparing)
            {
                preparing.Events.Add(args);
                return;
            }
            Event?.Invoke(this, args);
        }

        private sealed class ClosureMemberState
        {
            public string Xid { get; set; } = string.Empty;
            public string GroupId { get; set; } = string.Empty;
            public string ResourceId { get; set; } = string.Empty;
            public string VersionId { get; set; } = string.Empty;
            public WoTDocumentKindEnum Kind { get; set; }
            public ByteString ContentDigest { get; set; } = [];
        }

        private sealed class ClosureState
        {
            public string Key { get; set; } = string.Empty;
            public WotProjectionHandle? Handle { get; set; }
            public byte[] AggregateDigest { get; set; } = [];
            public uint Generation { get; set; }
            public ImmutableArray<string> MemberXids { get; set; } = [];

            public ImmutableArray<ClosureMemberState> Members { get; set; } = [];

            public ImmutableArray<WotResourceProjection> PublishedMetadata { get; set; } = [];

            public ImmutableArray<string> ModelNamespaceUris { get; set; } = [];

            public ImmutableArray<WotBindingPlan> BindingPlans { get; set; }
                = [];

            public ImmutableArray<WotViewProjectionHandle> ViewHandles { get; set; }
                = [];
        }

        private sealed class ClosureOutcome
        {
            public ClosureOutcome(
                ImmutableArray<WoTResourceLoadResultDataType> results,
                List<WotResourceProjection> projections,
                int retired = 0,
                ByteString effectiveInputDigest = default)
            {
                Results = results;
                Projections = projections;
                Retired = retired;
                EffectiveInputDigest = effectiveInputDigest;
            }

            public ImmutableArray<WoTResourceLoadResultDataType> Results { get; }
            public List<WotResourceProjection> Projections { get; }
            public int Retired { get; }
            public ByteString EffectiveInputDigest { get; }
        }

        private readonly IWotRegistryService m_registry;
        private readonly ProjectionCaptureAdapter m_host;
        private readonly ViewCaptureAdapter m_viewHost;
        private readonly BinderCaptureAdapter m_binders;
        private readonly IWotDocumentConverter m_converter;
        private readonly ImmutableArray<IWotNodeSetContributor> m_nodeSetContributors;
        private readonly IWotNodeSetResolver? m_nodeSetResolver;
        private IWotNodeResolver? m_addressSpace;
        private readonly WotNodeSetConverterOptions m_converterOptions;
        private readonly SemaphoreSlim m_mutex = new(1, 1);
        private readonly Lock m_lifetimeLock = new();

        private readonly Dictionary<string, ClosureState> m_closures =
            new(StringComparer.Ordinal);

        private readonly HashSet<string> m_projectionNamespaceUris =
            new(StringComparer.Ordinal);

        private uint m_generation;
        private int m_activeOperations;
        private int m_disposed;
        private bool m_mutexDisposed;
    }
}
