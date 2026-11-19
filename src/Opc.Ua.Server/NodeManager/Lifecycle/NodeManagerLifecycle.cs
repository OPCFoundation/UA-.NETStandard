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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Schema;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;
using OperationLifetime = Opc.Ua.Server.LifecycleWriter.OperationLifetime;
using RetiredGeneration = Opc.Ua.Server.GenerationReclaimer.RetiredGeneration;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Default live NodeManager lifecycle provider owned by a <see cref="StandardServer"/>.
    /// </summary>
    public sealed partial class NodeManagerLifecycle : INodeManagerLifecycle, IDisposable
    {
        /// <summary>
        /// Creates the server-owned lifecycle provider with its time source.
        /// </summary>
        /// <param name="server">The directly constructed server.</param>
        /// <param name="timeProvider">The server's clock.</param>
        internal NodeManagerLifecycle(StandardServer server, TimeProvider timeProvider)
            : this(server, timeProvider, null)
        {
        }

        /// <summary>
        /// Creates the server-owned lifecycle provider with a construction-bound writer factory.
        /// </summary>
        /// <param name="server">The directly constructed server.</param>
        /// <param name="timeProvider">The server's clock.</param>
        /// <param name="writerFactory">The factory receiving the owner's cleanup-completion policy.</param>
        internal NodeManagerLifecycle(
            StandardServer server,
            TimeProvider timeProvider,
            Func<Func<bool>, LifecycleWriter>? writerFactory)
            : this((INodeManagerLifecycleHost)server, timeProvider, writerFactory)
        {
        }

        /// <summary>
        /// Creates a lifecycle owner bound to its server services before any preparation or publication.
        /// </summary>
        /// <param name="server">The server state and schema services.</param>
        /// <param name="timeProvider">The server's clock.</param>
        /// <param name="writerFactory">The factory receiving the owner's cleanup-completion policy.</param>
        internal NodeManagerLifecycle(
            INodeManagerLifecycleHost server,
            TimeProvider timeProvider,
            Func<Func<bool>, LifecycleWriter>? writerFactory)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_reclaimer = new GenerationReclaimer(m_timeProvider);
            var weakOwner = new WeakReference<NodeManagerLifecycle>(this);
            bool CanDispose() => !weakOwner.TryGetTarget(out NodeManagerLifecycle? owner) ||
                (owner.m_reclaimer.Count == 0 &&
                    owner.m_registrations.Count == 0 &&
                    owner.PendingPrivateCleanupCount == 0);

            if (writerFactory is null)
            {
                m_writer = new LifecycleWriter(CanDispose);
            }
            else
            {
                m_writer = writerFactory(CanDispose)
                    ?? throw new InvalidOperationException("The lifecycle writer factory returned no writer.");
            }
        }

        /// <inheritdoc/>
        public ArrayOf<NodeManagerRegistration> Registrations
        {
            get
            {
                lock (m_registrationLock)
                {
                    return new ArrayOf<NodeManagerRegistration>(
                        m_registrations.Values
                            .Where(state => !state.IsSystem)
                            .Select(state => state.Registration)
                            .ToArray());
                }
            }
        }

        /// <inheritdoc/>
        public long Revision
        {
            get
            {
                return m_server.CurrentInstance.NodeManager is IDynamicNodeManagerBatchHost batchHost
                    ? batchHost.RoutingRevision.Revision
                    : 0;
            }
        }

        /// <inheritdoc/>
        public ArrayOf<NodeManagerSlot> Slots
        {
            get
            {
                lock (m_registrationLock)
                {
                    return new ArrayOf<NodeManagerSlot>(
                        m_registrations.Values
                            .Where(state => !state.IsSystem)
                            .Select(state => state.Registration.Slot)
                            .Where(slot => slot.TryGetCurrent(out _))
                            .ToArray());
                }
            }
        }

        /// <inheritdoc/>
        public bool IsShuttingDown => m_writer.IsShuttingDown;

        internal long ShutdownCleanupProgress =>
            m_reclaimer.CleanupProgress;

        internal int RetiredNodeManagerCount => m_reclaimer.Count;

        /// <summary>
        /// Gets the number of private batch cleanup owners still retaining candidates.
        /// </summary>
        private int PendingPrivateCleanupCount
        {
            get
            {
                lock (m_privateCleanupLock)
                {
                    return m_privateCleanups.Count;
                }
            }
        }

        /// <summary>
        /// Binds the host's exact recovery adapters once before the lifecycle is exposed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="authority"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The supplied authority does not match an existing or sealed cleanup-authority binding.
        /// </exception>
        internal void BindCleanupAuthority(NodeManagerCleanupAuthority authority)
        {
            if (authority is null)
            {
                throw new ArgumentNullException(nameof(authority));
            }
            lock (m_cleanupAuthorityLock)
            {
                if (m_cleanupAuthority is null && !m_cleanupAuthoritySealed)
                {
                    m_cleanupAuthority = authority;
                }
                else if (m_cleanupAuthority is null || !m_cleanupAuthority.Matches(authority))
                {
                    throw new InvalidOperationException("The server's cleanup recovery authority is already bound.");
                }
            }
        }

        /// <summary>
        /// Prevents installing recovery adapters after server startup begins.
        /// </summary>
        internal void SealCleanupAuthority()
        {
            lock (m_cleanupAuthorityLock)
            {
                m_cleanupAuthority ??= new NodeManagerCleanupAuthority(null, null, null);
                m_cleanupAuthoritySealed = true;
            }
        }

        /// <summary>
        /// Requires the adapters configured by the owning host.
        /// </summary>
        /// <exception cref="UnauthorizedAccessException">
        /// Cleanup authority is unbound or the supplied authority does not match the owning host's adapters.
        /// </exception>
        internal void DemandCleanupAuthority(NodeManagerCleanupAuthority authority)
        {
            lock (m_cleanupAuthorityLock)
            {
                if (m_cleanupAuthority is null || !m_cleanupAuthority.Matches(authority))
                {
                    throw new UnauthorizedAccessException(
                        "The cleanup recovery authority is not bound to this server.");
                }
            }
        }

        /// <summary>
        /// Gets whether a serving or retired generation still requires the server internals.
        /// </summary>
        internal bool HasPendingShutdownCleanup
        {
            get
            {
                lock (m_registrationLock)
                {
                    return m_registrations.Count != 0 ||
                        m_reclaimer.Count != 0 ||
                        PendingPrivateCleanupCount != 0;
                }
            }
        }

        /// <summary>
        /// Captures the published and retired generations still owned by this lifecycle.
        /// </summary>
        /// <returns>The owned generations, without transferring their cleanup ownership.</returns>
        internal NodeManagerGeneration[] SnapshotCleanupGenerations()
        {
            NodeManagerGeneration[] registrations;
            lock (m_registrationLock)
            {
                registrations = [.. m_registrations.Values.Select(state => state.Registration.Identity)];
            }

            NodeManagerGeneration[] privateCandidates;
            lock (m_privateCleanupLock)
            {
                privateCandidates =
                [
                    .. m_privateCleanups.SelectMany(cleanup => cleanup.Candidates)
                        .Select(candidate => candidate.Generation)
                ];
            }

            return
            [
                .. registrations.Concat(m_reclaimer.Snapshot().Select(retired => retired.Generation))
                    .Concat(privateCandidates)
                    .Distinct()
            ];
        }

        /// <summary>
        /// Gets the first retirement failure retained for an exact generation.
        /// </summary>
        /// <param name="generation">The retired generation to inspect.</param>
        /// <returns>The reported failure, or null if none has been reported.</returns>
        internal Exception? GetRetirementFailure(NodeManagerGeneration generation)
        {
            return m_reclaimer.GetRetirementFailure(generation);
        }

        /// <summary>
        /// Requests another eligible retirement attempt after a verified resource cleanup.
        /// </summary>
        /// <remarks>
        /// During shutdown the server must resume its ordered shutdown instead of starting normal scheduling.
        /// </remarks>
        internal bool TryRetryCleanupAfterConfirmation()
        {
            if (IsShuttingDown)
            {
                return false;
            }
            m_reclaimer.RetryReady();
            m_reclaimer.RetryConfirmed();
            return !IsShuttingDown;
        }

        internal void PrepareForStartup()
        {
            m_writer.BeginStartup(() =>
            {
                if (PendingPrivateCleanupCount != 0)
                {
                    throw new InvalidOperationException("Private candidate cleanup is still retained.");
                }
                m_reclaimer.BeginStartup();
            });
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            ThrowIfDecisionCallbackActive();

            // Signal only: Dispose is synchronous. A drain already running
            // finishes retiring the generations it captured.
            m_reclaimer.Dispose();

            m_writer.Dispose();
        }

        /// <summary>
        /// Prepares a lifecycle that was previously shut down to accept operations again after a server restart.
        /// </summary>
        internal void BeginStartup()
        {
            ThrowIfDecisionCallbackActive();

            m_writer.BeginStartup(() =>
            {
                lock (m_registrationLock)
                {
                    if (m_registrations.Count != 0 || m_reclaimer.Count != 0)
                    {
                        throw new InvalidOperationException(
                            "The previous NodeManager generations have not completed cleanup.");
                    }
                    m_reclaimer.BeginStartup();
                }
            });
        }

        internal async ValueTask BeginShutdownAsync(
            IServerInternal server,
            CancellationToken ct = default)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            Exception[] failures = await PrepareShutdownRegistrationsAsync(server, ct).ConfigureAwait(false);
            if (failures.Length != 0)
            {
                throw new AggregateException("Accepted NodeManager work failed to stop.", failures);
            }
        }

        private async ValueTask<Exception[]> PrepareShutdownRegistrationsAsync(
            IServerInternal server,
            CancellationToken ct)
        {
            Task activeOperations = await m_writer.BeginShutdownAsync(ct).ConfigureAwait(false);
            bool shutdownPrepared = false;
            try
            {
                await LifecycleWaitDiagnostics.WaitAsync(
                    activeOperations,
                    m_timeProvider,
                    LifecycleWaitDiagnostics.WarningThreshold,
                    elapsed => server.Telemetry.CreateLogger<NodeManagerLifecycle>()
                        .LifecycleWaitExceeded(
                            "NodeManager lifecycle operations before shutdown",
                            elapsed,
                            GetLifecycleWaitContext()),
                    ct).ConfigureAwait(false);
                RegistrationState[] registrations;
                lock (m_registrationLock)
                {
                    registrations = m_registrations.Values.ToArray();
                }
                var failures = new List<Exception>();
                foreach (RegistrationState registration in registrations)
                {
                    ct.ThrowIfCancellationRequested();
                    if (registration.Prepared.NodeManager is INodeManagerShutdown)
                    {
                        try
                        {
                            await PrepareShutdownRegistrationAsync(registration).ConfigureAwait(false);
                        }
                        catch (Exception failure) when (failure is not OutOfMemoryException)
                        {
                            failures.Add(failure);
                        }
                    }
                }
                shutdownPrepared = failures.Count == 0;
                return [.. failures];
            }
            finally
            {
                m_writer.EndShutdown(shutdownPrepared);
            }
        }

        /// <summary>
        /// Shares active preparation and retains success for this exact registration's shutdown retries.
        /// </summary>
        /// <param name="registration">The registration whose producers must stop.</param>
        /// <returns>The shared preparation task, replaced only after a failed attempt.</returns>
        private Task PrepareShutdownRegistrationAsync(
            RegistrationState registration)
        {
            lock (m_registrationLock)
            {
                if (registration.ShutdownPreparation is null or { IsFaulted: true } or { IsCanceled: true })
                {
                    registration.ShutdownPreparation = PrepareShutdownGenerationAsync(
                        registration.Registration.Identity);
                }
                return registration.ShutdownPreparation;
            }
        }

        private static bool IsShutdownPrepared(RegistrationState registration)
        {
            return registration.Prepared.NodeManager is not INodeManagerShutdown ||
                registration.ShutdownPreparation is { Status: TaskStatus.RanToCompletion };
        }

        /// <summary>
        /// Stops one generation's producers while retaining source admission for their final callbacks.
        /// </summary>
        /// <param name="generation">The generation retained until producer preparation completes.</param>
        /// <returns>A task completed after producer preparation and generation-admission release.</returns>
        private static async Task PrepareShutdownGenerationAsync(
            NodeManagerGeneration generation)
        {
            await Task.Yield();
            if (!generation.Gate.TryEnter(out int stripe))
            {
                return;
            }
            try
            {
                if (!generation.ResourceOwnership.IsShared)
                {
                    await generation.PrepareForShutdownAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                generation.Gate.Exit(stripe);
            }
        }

        internal ValueTask CompleteShutdownAsync(
            IServerInternal server,
            CancellationToken ct = default)
        {
            return CompleteShutdownAsync(server, retireSystems: false, ct);
        }

        /// <summary>
        /// Withdraws owned generations, including system roles only after the server has stopped their consumers.
        /// </summary>
        /// <exception cref="NodeManagerCleanupDeferredException">
        /// Private candidate cleanup is still pending.
        /// </exception>
        /// <exception cref="AggregateException">
        /// One or more generation shutdown preparations fail, optionally together with private cleanup.
        /// </exception>
        internal async ValueTask CompleteShutdownAsync(
            IServerInternal server,
            bool retireSystems,
            CancellationToken ct = default)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            Exception[] preparationFailures = await PrepareShutdownRegistrationsAsync(server, ct)
                .ConfigureAwait(false);
            Task activeOperations = await m_writer.BeginShutdownAsync(ct).ConfigureAwait(false);
            LifecycleWriter.ShutdownCleanup? cleanupOwnership = null;
            bool shutdownCompleted = false;
            try
            {
                await LifecycleWaitDiagnostics.WaitAsync(
                    activeOperations,
                    m_timeProvider,
                    LifecycleWaitDiagnostics.WarningThreshold,
                    elapsed => server.Telemetry.CreateLogger<NodeManagerLifecycle>()
                        .LifecycleWaitExceeded(
                            "NodeManager lifecycle operations before shutdown",
                            elapsed,
                            GetLifecycleWaitContext()),
                    ct).ConfigureAwait(false);
                LifecycleWriter.ShutdownCleanup shutdownCleanup =
                    await m_writer.BeginShutdownCleanupAsync(ct).ConfigureAwait(false);
                cleanupOwnership = shutdownCleanup;
                m_reclaimer.StopScheduling();
                ArrayOf<LifecycleChange> applications;
                lock (m_registrationLock)
                {
                    applications = m_registrations.Values.Where(state => !state.IsSystem)
                        .Where(IsShutdownPrepared)
                        .Select(state => LifecycleChange.Remove(state.Registration.Slot)).ToArrayOf();
                }
                PrivateBatchCleanup[] decidedCandidates;
                lock (m_privateCleanupLock)
                {
                    decidedCandidates =
                    [
                        .. m_privateCleanups.Where(cleanup => cleanup.Stage == PrivateCleanupStage.WaitingForShutdown)
                    ];
                    foreach (PrivateBatchCleanup cleanup in decidedCandidates)
                    {
                        cleanup.Stage = PrivateCleanupStage.Reserving;
                    }
                }
                foreach (PrivateBatchCleanup cleanup in decidedCandidates)
                {
                    _ = await StartPrivateBatchCleanupAsync(server, cleanup).ConfigureAwait(false);
                }
                await CompleteShutdownBatchAsync(server, applications, shutdownCleanup, ct).ConfigureAwait(false);
                Task? privateCleanup = CapturePrivateCleanupCompletion();
                if (privateCleanup is not null)
                {
                    if (!privateCleanup.IsCompleted)
                    {
                        Exception? preparationFailure = preparationFailures.Length == 0
                            ? null
                            : new AggregateException(
                                "One or more NodeManager shutdown preparations failed.",
                                preparationFailures);
                        throw new NodeManagerCleanupDeferredException(privateCleanup, preparationFailure);
                    }
                    try
                    {
                        await privateCleanup.ConfigureAwait(false);
                    }
                    catch (Exception failure) when (
                        preparationFailures.Length != 0 && failure is not OutOfMemoryException)
                    {
                        throw new AggregateException(
                            "Private cleanup and NodeManager shutdown preparation failed.",
                            new[] { failure }.Concat(preparationFailures));
                    }
                }

                ArrayOf<LifecycleChange> systems = [];
                if (retireSystems && m_reclaimer.Count == 0)
                {
                    lock (m_registrationLock)
                    {
                        if (m_registrations.Values.All(state => state.IsSystem))
                        {
                            systems = m_registrations.Values.Where(state => state.IsSystem)
                                .Where(IsShutdownPrepared)
                                .Select(state => LifecycleChange.Remove(state.Registration.Slot)).ToArrayOf();
                        }
                    }
                }
                await CompleteShutdownBatchAsync(server, systems, shutdownCleanup, ct).ConfigureAwait(false);
                if (preparationFailures.Length != 0)
                {
                    throw new AggregateException(
                        "One or more NodeManager shutdown preparations failed.",
                        preparationFailures);
                }
                shutdownCompleted = true;
            }
            finally
            {
                cleanupOwnership?.Dispose();
                m_writer.EndShutdown(!shutdownCompleted, completing: true);
            }
        }

        private async ValueTask CompleteShutdownBatchAsync(
            IServerInternal server,
            ArrayOf<LifecycleChange> changes,
            LifecycleWriter.ShutdownCleanup cleanupOwnership,
            CancellationToken ct)
        {
            Exception? reconciliationFailure = null;
            if (changes.Count != 0)
            {
                var shutdown = new ShutdownPublication(this, server, cleanupOwnership);
                IPreparedChanges batch = await PrepareChangesAsync(
                    changes,
                    null,
                    CaptureLifecycleCallbackPermit(),
                    ct,
                    shutdown: shutdown).ConfigureAwait(false);
                await using var lifetime = batch.ConfigureAwait(false);
                LifecycleResult result = await batch.CommitAsync(cancellationToken: ct).ConfigureAwait(false);
                if (result.CleanupFailure is { } failure)
                {
                    reconciliationFailure = new InvalidOperationException(
                        "Shutdown removal reconciliation failed.", failure);
                }
            }

            try
            {
                await m_reclaimer.CompleteShutdownAsync(ct).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure) when (
                reconciliationFailure is not null &&
                cleanupFailure is not OutOfMemoryException and not OperationCanceledException)
            {
                throw new AggregateException(
                    "Shutdown reconciliation and retirement cleanup failed.",
                    reconciliationFailure,
                    cleanupFailure);
            }

            if (reconciliationFailure is null)
            {
                return;
            }
            if (m_reclaimer.Count == 0)
            {
                server.Telemetry.CreateLogger<NodeManagerLifecycle>()
                    .ShutdownReconciliationReported(reconciliationFailure);
                return;
            }
            throw reconciliationFailure;
        }

        /// <summary>
        /// Notifies a registered node manager outside mutation serialization and revalidates its registration.
        /// </summary>
        /// <param name="server">The running server.</param>
        /// <param name="host">The dynamic node manager host.</param>
        /// <param name="state">The registration being completed.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <param name="initial">The explicit startup capability, or null for runtime readiness.</param>
        /// <returns>A task that completes when the readiness callback finished and the registration is still
        /// current.</returns>
        private async ValueTask CompleteReadinessAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            RegistrationState state,
            CancellationToken ct,
            InitialPublication? initial = null)
        {
            bool releaseReadiness = false;
            if (state.ReadinessPending)
            {
                lock (m_registrationLock)
                {
                    if (state.ReadinessPending)
                    {
                        if (!m_registrations.TryGetValue(
                            state.Registration.Id,
                            out RegistrationState? current) ||
                            !ReferenceEquals(current, state))
                        {
                            throw new InvalidOperationException(
                                "The registration changed before its readiness operation started.");
                        }
                        if (!state.ReadinessInProgress)
                        {
                            state.ReadinessInProgress = true;
                            releaseReadiness = true;
                        }
                    }
                }
            }

            try
            {
                if (state.ReadinessPending)
                {
                    if (!NodeManagerReadiness.IsParticipant(state.Prepared.NodeManager) ||
                        state.Prepared.NodeManager is not INodeManagerReadinessNotification participant)
                    {
                        throw new InvalidOperationException(
                            "A registration pending readiness does not expose a readiness callback.");
                    }

                    ct.ThrowIfCancellationRequested();
                    using var readiness = new NodeManagerReadinessScope(server, state.Registration.Identity);
                    string readinessContext = $"{state.Registration.Identity.GetQuiescenceWaitContext()}; " +
                        GetLifecycleWaitContext();
                    await LifecycleWaitDiagnostics.WaitAsync(
                        participant.OnServerReadyAsync(ct).AsTask(),
                        m_timeProvider,
                        LifecycleWaitDiagnostics.WarningThreshold,
                        elapsed => server.Telemetry.CreateLogger<NodeManagerLifecycle>()
                            .LifecycleWaitExceeded(
                                "NodeManager readiness callback",
                                elapsed,
                                readinessContext),
                        ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (state.Prepared.NodeManager is FluentNodeManagerBase fluentNodeManager)
                    {
                        await fluentNodeManager.CompleteDeferredConfigurationForLifecycleAsync(ct)
                        .ConfigureAwait(false);
                    }
                    ct.ThrowIfCancellationRequested();

                    EnsurePublicationServer(server, host, initial);
                    if (!IsCurrentRegistration(state.Registration))
                    {
                        throw new InvalidOperationException(
                            "The registration changed while its readiness operation was completing.");
                    }

                    CompleteReadinessClaim(
                        state,
                        () =>
                        {
                            state.HistorianRegistrationsPublished =
                                ChangePreparer.PublishHistorianRegistrations(state.Prepared.NodeManager);
                        });
                }
                else
                {
                    state.HistorianRegistrationsPublished =
                        ChangePreparer.PublishHistorianRegistrations(state.Prepared.NodeManager);
                }

                await LifecycleWaitDiagnostics.WaitAsync(
                    state.Registration.Identity.FlushDeferredEventsAsync(),
                    m_timeProvider,
                    LifecycleWaitDiagnostics.WarningThreshold,
                    elapsed => server.Telemetry.CreateLogger<NodeManagerLifecycle>()
                        .LifecycleWaitExceeded(
                            "NodeManager readiness event flush",
                            elapsed,
                            state.Registration.Identity.GetQuiescenceWaitContext()))
                    .ConfigureAwait(false);
            }
            finally
            {
                if (releaseReadiness)
                {
                    lock (m_registrationLock)
                    {
                        state.ReadinessInProgress = false;
                    }
                }
            }
        }

        /// <summary>
        /// Gets current generation pin details when lifecycle operations delay shutdown.
        /// </summary>
        /// <returns>The active lifecycle-operation count and non-quiescent generation identities.</returns>
        private string GetLifecycleWaitContext()
        {
            RegistrationState[] registrations;
            string[] pendingReadiness;
            int pendingReadinessCount;
            lock (m_registrationLock)
            {
                registrations = [.. m_registrations.Values];
                pendingReadinessCount = registrations.Count(static state => state.ReadinessPending);
                pendingReadiness = registrations
                    .Where(static state => state.ReadinessPending)
                    .Take(8)
                    .Select(static state =>
                        $"slot={state.Registration.Slot.Id}; generation={state.Registration.Generation}; " +
                        $"manager={state.Prepared.NodeManager.GetType().FullName}; " +
                        $"callbackInProgress={state.ReadinessInProgress}")
                    .ToArray();
            }
            RegistrationState[] blockedRegistrations = registrations
                .Where(static state => state.Registration.Identity.HasActiveQuiescenceBlockers)
                .ToArray();
            string[] generations = blockedRegistrations
                .Select(static state => state.Registration.Identity)
                .Take(8)
                .Select(static generation => generation.GetQuiescenceWaitContext())
                .ToArray();
            return $"activeLifecycleOperations={m_writer.ActiveOperationCount}; " +
                $"pendingReadinessCount={pendingReadinessCount}; " +
                $"pendingReadiness=[{string.Join("; ", pendingReadiness)}]; " +
                $"blockedGenerationCount={blockedRegistrations.Length}; " +
                $"blockedGenerations=[{string.Join("; ", generations)}]";
        }

        /// <summary>
        /// Opens a generation after its readiness callback completed successfully.
        /// </summary>
        /// <param name="state">The registration whose readiness completed.</param>
        /// <param name="beforeClaim">A synchronous publication step performed before readiness is released.</param>
        private void CompleteReadinessClaim(RegistrationState state, Action? beforeClaim = null)
        {
            lock (m_registrationLock)
            {
                if (!m_registrations.TryGetValue(
                    state.Registration.Id,
                    out RegistrationState? current) ||
                    !ReferenceEquals(current, state))
                {
                    throw new InvalidOperationException(
                        "The registration changed before its readiness operation could be published.");
                }
                beforeClaim?.Invoke();
                state.ReadinessPending = false;
            }
            NodeManagerGeneration generation = state.Registration.Identity;
            foreach (KeyValuePair<IMonitoredItem, ISubscription> entry in generation.MonitoredItems.ToArray())
            {
                if (entry.Key is MonitoredItem native)
                {
                    native.CompleteReadiness(generation);
                }
            }
        }

        private (IServerInternal Server, IDynamicNodeManagerHost Host) GetRunningServer()
        {
            m_writer.EnsureRunning();

            IServerInternal server = m_server.CurrentInstance;
            if (!server.IsRunning && !NodeManagerReadinessScope.Allows(server))
            {
                throw new InvalidOperationException(
                    "NodeManagers can only be changed while the server is running.");
            }

            if (server.NodeManager is not IDynamicNodeManagerHost host)
            {
                throw new NotSupportedException(
                    "The configured master NodeManager does not support live lifecycle operations.");
            }
            return (server, host);
        }

        /// <summary>
        /// Enters a lifecycle operation that is tracked until it is disposed.
        /// </summary>
        /// <returns>The scope that ends the operation when disposed.</returns>
        private OperationLifetime EnterLifecycleOperation()
        {
            return m_writer.EnterOperation();
        }

        private NodeManagerCallbackPermit? CaptureLifecycleCallbackPermit()
        {
            return m_server.CurrentInstance.NodeManager is IDynamicNodeManagerBatchHost batchHost
                ? batchHost.CaptureLifecycleCallbackPermit()
                : null;
        }

        private void EnsureSameRunningServer(
            IServerInternal server,
            IDynamicNodeManagerHost host)
        {
            (IServerInternal currentServer, IDynamicNodeManagerHost currentHost) =
                GetRunningServer();
            if (!ReferenceEquals(currentServer, server) ||
                !ReferenceEquals(currentHost, host))
            {
                throw new InvalidOperationException(
                    "The running server changed during the lifecycle operation.");
            }
        }

        private bool IsCurrentRegistration(NodeManagerRegistration registration)
        {
            lock (m_registrationLock)
            {
                return m_registrations.TryGetValue(
                    registration.Id,
                    out RegistrationState? state) &&
                    ReferenceEquals(state.Registration.Identity, registration.Identity);
            }
        }

        private bool IsOwnedNodeManager(IAsyncNodeManager nodeManager)
        {
            lock (m_registrationLock)
            {
                return m_registrations.Values.Any(state =>
                        NodeManagerGenerationRegistry.HasSameGenerationIdentity(
                            state.Registration.NodeManager,
                            nodeManager)) ||
                    m_reclaimer.Snapshot().Any(retired =>
                        NodeManagerGenerationRegistry.HasSameGenerationIdentity(
                            retired.NodeManager,
                            nodeManager));
            }
        }

        /// <summary>
        /// Verifies that a registration-bound factory belongs to the operation being prepared.
        /// </summary>
        /// <param name="factory">The factory to validate.</param>
        /// <param name="registration">The registration the operation targets, or null.</param>
        private static void ValidateFactoryRegistration(
            IAsyncNodeManagerFactory factory, NodeManagerRegistration? registration)
        {
            if (factory is IRegistrationBoundNodeManagerFactory bound &&
                !ReferenceEquals(bound.ExpectedRegistration, registration))
            {
                throw new ArgumentException(
                    "The factory does not belong to this exact addition or replacement operation.", nameof(factory));
            }
        }

        private RegistrationState GetCurrentState(
            NodeManagerRegistration registration,
            bool allowPendingReadiness = false,
            bool allowRemovalAfterFailedReadiness = false)
        {
            lock (m_registrationLock)
            {
                if (!m_registrations.TryGetValue(
                    registration.Id,
                    out RegistrationState? state) ||
                    !ReferenceEquals(state.Registration.Identity, registration.Identity))
                {
                    throw new InvalidOperationException(
                        "The registration is stale or is not owned by this lifecycle provider.");
                }
                if (state.ReadinessPending &&
                    !allowPendingReadiness &&
                    (!allowRemovalAfterFailedReadiness || state.ReadinessInProgress))
                {
                    throw new InvalidOperationException(
                        "The NodeManager is completing readiness. Retry after its startup operation completes.");
                }
                return state;
            }
        }

        /// <summary>
        /// Detaches and invalidates monitored items left on a quiescent retired manager.
        /// </summary>
        /// <param name="server">The owning server.</param>
        /// <param name="nodeManager">The retired manager.</param>
        /// <returns>The asynchronous transition.</returns>
        internal static async ValueTask DetachActiveMonitoredItemsAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            MonitoredItemTransition monitoredItemTransition =
                await PrepareMonitoredItemTransitionAsync(
                    nodeManager,
                    replacement: null,
                    CancellationToken.None).ConfigureAwait(false);
            List<Exception> failures = await monitoredItemTransition
                .AdoptAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                monitoredItemTransition.MarkDeletedItems();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Monitored items left on the retired manager could not be finalized.",
                    failures);
            }
        }

        /// <summary>
        /// Moves late items to an admitted current generation unless a committed invalidation cut off migration.
        /// Target admission outlives writer ownership, so retirement discovers items adopted after its inventory.
        /// </summary>
        /// <param name="retired">The quiescent source and its committed migration policy.</param>
        /// <returns>The asynchronous migration.</returns>
        private async ValueTask MigrateLateMonitoredItemsAsync(RetiredGeneration retired)
        {
            if (!HasActiveMonitoredItems(retired.Server, retired.NodeManager) &&
                !retired.Generation.HasPendingSourceReleases)
            {
                return;
            }

            NodeManagerGeneration target;
            int stripe;
            using (await m_writer.BeginMutationAsync(CancellationToken.None).ConfigureAwait(false))
            {
                NodeManagerSlot slot = retired.Generation.Slot;
                if (slot.LateMigrationRevision != retired.LateMigrationRevision || slot.Current is not { } current)
                {
                    return;
                }
                target = current;
                if (!target.Gate.TryEnter(out stripe))
                {
                    throw new InvalidOperationException("The published late migration target is not admitting work.");
                }
            }
            try
            {
                await MigrateLateMonitoredItemsAsync(
                    retired.Server, retired.NodeManager, target.NodeManager).ConfigureAwait(false);
            }
            finally
            {
                target.Gate.Exit(stripe);
            }
        }

        /// <summary>
        /// Migrates late items after the caller has admitted the replacement generation.
        /// </summary>
        /// <param name="server">The owning server.</param>
        /// <param name="retired">The retired source.</param>
        /// <param name="replacement">The admitted replacement.</param>
        /// <returns>The asynchronous migration.</returns>
        internal static async ValueTask MigrateLateMonitoredItemsAsync(
            IServerInternal server,
            IAsyncNodeManager retired,
            IAsyncNodeManager replacement)
        {
            if (!HasActiveMonitoredItems(server, retired) &&
                !NodeManagerGenerationRegistry.GetOrCreate(retired).HasPendingSourceReleases)
            {
                return;
            }

            MonitoredItemTransition monitoredItemTransition =
                await PrepareMonitoredItemTransitionAsync(
                    retired,
                    replacement,
                    CancellationToken.None).ConfigureAwait(false);
            List<Exception> failures =
                await monitoredItemTransition.AdoptAsync(
                    CancellationToken.None).ConfigureAwait(false);
            try
            {
                monitoredItemTransition.MarkDeletedItems();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failures.Add(failure);
            }
            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Monitored items created during the reload could not be migrated.",
                    failures);
            }
        }

        /// <summary>
        /// Selects one generation-owned item inventory for migration or terminal removal without placeholder sources.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// Monitored items require a transition, but the current or replacement manager does not implement
        /// <see cref="INodeManagerMonitoredItemSource"/>.
        /// </exception>
        private static async ValueTask<MonitoredItemTransition>
            PrepareMonitoredItemTransitionAsync(
                IAsyncNodeManager current,
                IAsyncNodeManager? replacement,
                CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(current);
            var ownedItems = new HashSet<IMonitoredItem>(generation.MonitoredItems.Keys.Where(item =>
                (item.MonitoredItemType & MonitoredItemTypeMask.AllEvents) == 0 &&
                item is not IMonitoredItemGenerationBinding { IsSourceUnavailable: true }),
                MonitoredItemReferenceComparer.Instance);
            foreach (IMonitoredItem item in generation.CapturePendingSourceItems())
            {
                if ((item.MonitoredItemType & MonitoredItemTypeMask.AllEvents) == 0 &&
                    item is IMonitoredItemGenerationBinding { IsRemoved: false, IsTerminal: false } binding &&
                    binding.CaptureSource().Generation is null &&
                    item.SubscriptionCallback is ISubscriptionMonitoredItemLifecycle subscription &&
                    subscription.ContainsMonitoredItem(item))
                {
                    ownedItems.Add(item);
                }
            }
            IMonitoredItem[] items = [.. ownedItems];
            var currentLifecycle = current as INodeManagerMonitoredItemSource;
            if (currentLifecycle is null && items.Length != 0)
            {
                throw new NotSupportedException("The NodeManager does not support monitored-item transitions.");
            }
            if (replacement is null || items.Length == 0)
            {
                return new MonitoredItemTransition(
                    currentLifecycle,
                    replacement: null,
                    compatibleItems: [],
                    deletedItems: items,
                    terminal: replacement is null);
            }
            if (replacement is not INodeManagerMonitoredItemSource replacementLifecycle)
            {
                throw new NotSupportedException(
                    "The replacement NodeManager does not support monitored-item transitions.");
            }

            var compatibleItems = new List<IMonitoredItem>(items.Length);
            var deletedItems = new List<IMonitoredItem>();
            foreach (IMonitoredItem monitoredItem in items)
            {
                ServiceResult result = await replacementLifecycle
                    .ValidateSourceAsync(monitoredItem, ct)
                    .ConfigureAwait(false);
                if (ServiceResult.IsGood(result))
                {
                    compatibleItems.Add(monitoredItem);
                }
                else if (IsExpectedMonitoredItemIncompatibility(result))
                {
                    deletedItems.Add(monitoredItem);
                }
                else
                {
                    throw new ServiceResultException(result);
                }
            }

            return new MonitoredItemTransition(
                currentLifecycle,
                replacementLifecycle,
                compatibleItems,
                deletedItems);
        }

        private static bool IsExpectedMonitoredItemIncompatibility(ServiceResult result)
        {
            StatusCode statusCode = result.StatusCode;
            return statusCode == StatusCodes.BadNodeIdUnknown ||
                statusCode == StatusCodes.BadAttributeIdInvalid ||
                statusCode == StatusCodes.BadDataEncodingInvalid ||
                statusCode == StatusCodes.BadDataEncodingUnsupported ||
                statusCode == StatusCodes.BadFilterNotAllowed ||
                statusCode == StatusCodes.BadFilterOperandInvalid ||
                statusCode == StatusCodes.BadFilterOperatorInvalid ||
                statusCode == StatusCodes.BadFilterOperatorUnsupported ||
                statusCode == StatusCodes.BadFilterOperandCountMismatch ||
                statusCode == StatusCodes.BadFilterElementInvalid ||
                statusCode == StatusCodes.BadFilterLiteralInvalid;
        }

        private static ValueTask ResolveUnresolvedSourcesAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            return server.NodeManager is IDynamicNodeManagerHost recovery
                ? recovery.ResolveUnresolvedSourcesAsync(
                    nodeManager,
                    cancellationToken: ct)
                : default;
        }

        /// <summary>
        /// Verifies that the server's subscriptions no longer retain items on the manager.
        /// </summary>
        /// <param name="server">The owning server.</param>
        /// <param name="nodeManager">The manager whose items must have drained.</param>
        internal static void EnsureNoActiveMonitoredItems(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            if (HasActiveMonitoredItems(server, nodeManager))
            {
                throw new InvalidOperationException(
                    "The NodeManager cannot be reloaded or removed while it owns monitored items.");
            }
        }

        /// <summary>
        /// Tests subscription ownership without invoking manager callbacks.
        /// </summary>
        /// <param name="server">The owning server.</param>
        /// <param name="nodeManager">The manager whose ownership is tested.</param>
        /// <returns>Whether an active subscription retains manager-owned items.</returns>
        internal static bool HasActiveMonitoredItems(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(nodeManager);
            foreach (IMonitoredItem item in generation.MonitoredItems.Keys)
            {
                if ((item.MonitoredItemType & MonitoredItemTypeMask.AllEvents) == 0 &&
                    item is not IMonitoredItemGenerationBinding { IsSourceUnavailable: true })
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Requests independent reclamation after a host-owned generation admission drains.
        /// </summary>
        private void ScheduleRetiredGenerationDrainCleanup()
        {
            m_reclaimer.Schedule();
        }

        private static ValueTask RebindIdentityAsync(IServerInternal server, CancellationToken cancellationToken)
        {
            return server is INodeIdFactoryProvider { NodeIdFactory: INodeIdFactoryPolicy policy }
                ? policy.OnNodeManagersChangedAsync(server, cancellationToken)
                : default;
        }

        /// <summary>
        /// Gets the reconciler owned by the server's node-manager host.
        /// </summary>
        /// <param name="server">The server that owns the generation bindings.</param>
        /// <returns>The server-owned binding reconciler.</returns>
        private static GenerationReconciler GetGenerationReconciler(IServerInternal server)
        {
            return server.NodeManager is IDynamicNodeManagerHost host
                ? host.GenerationReconciler
                : throw new InvalidOperationException(
                    "The NodeManager host does not provide generation binding reconciliation.");
        }

        private static NodeManagerGeneration GetCurrentGeneration(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            if (server.NodeManager is MasterNodeManager master &&
                master.TryGetCurrentGeneration(
                    nodeManager,
                    out NodeManagerGeneration? generation))
            {
                return generation!;
            }
            throw new InvalidOperationException(
                "The NodeManager is not present in the current routing view.");
        }

        private static async ValueTask ValidateDataTypeCompatibilityAsync(
            ChangePreparer.StagingServer server,
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            if (nodeManager is not RuntimeNodeSetNodeManager runtimeNodeManager)
            {
                return;
            }

            IReadOnlyDictionary<NodeId, DataTypeDefinition>? completedDefinitions = null;
            foreach (KeyValuePair<NodeId, DataTypeDefinition> entry in
                runtimeNodeManager.GetDataTypeDefinitions())
            {
                var typeId = NodeId.ToExpandedNodeId(
                    entry.Key,
                    server.NamespaceUris);
                IDataTypeDefinitionSource? definitionSource = null;

                if (server.Factory.TryGetEncodeableType(
                    typeId,
                    out IEncodeableType? encodeableType))
                {
                    definitionSource = encodeableType as IDataTypeDefinitionSource;
                }
                else if (server.Factory.TryGetEnumeratedType(
                    typeId,
                    out IEnumeratedType? enumeratedType))
                {
                    definitionSource = enumeratedType as IDataTypeDefinitionSource;
                }

                if (definitionSource is not null)
                {
                    completedDefinitions ??= runtimeNodeManager.GetDataTypeDefinitions(completeMetadata: true);
                    if (!definitionSource
                        .GetDataTypeDefinition(server.NamespaceUris)
                        .IsEqual(completedDefinitions[entry.Key]))
                    {
                        throw new InvalidOperationException(
                            $"DataType '{entry.Key}' has an incompatible definition. " +
                            "Runtime DataType definitions are immutable for the server lifetime.");
                    }
                }

                if (definitionSource is null &&
                    await server.NodeManager
                        .FindNodeInAddressSpaceAsync(entry.Key, ct)
                        .ConfigureAwait(false) is DataTypeState existingDataType &&
                    existingDataType.DataTypeDefinition.TryGetValue(
                        out DataTypeDefinition? existingDefinition) &&
                    !existingDefinition.IsEqual(entry.Value))
                {
                    throw new InvalidOperationException(
                        $"DataType '{entry.Key}' has an incompatible definition. " +
                        "Runtime DataType definitions are immutable for the server lifetime.");
                }
            }

            RegisterCompatibleEncodingAliases(server, runtimeNodeManager);
        }

        private static void RegisterCompatibleEncodingAliases(
            ChangePreparer.StagingServer server,
            RuntimeNodeSetNodeManager runtimeNodeManager)
        {
            IEncodeableFactoryBuilder? builder = null;
            foreach (KeyValuePair<NodeId, ArrayOf<NodeId>> entry in
                runtimeNodeManager.GetDataTypeEncodings())
            {
                var dataTypeId = NodeId.ToExpandedNodeId(
                    entry.Key,
                    server.NamespaceUris);
                if (!server.Factory.TryGetEncodeableType(
                    dataTypeId,
                    out IEncodeableType? encodeableType))
                {
                    continue;
                }

                foreach (NodeId encodingId in entry.Value)
                {
                    var expandedEncodingId = NodeId.ToExpandedNodeId(
                        encodingId,
                        server.NamespaceUris);
                    if (server.Factory.TryGetEncodeableType(
                        expandedEncodingId,
                        out IEncodeableType? existingAlias))
                    {
                        if (!ReferenceEquals(existingAlias, encodeableType))
                        {
                            throw new InvalidOperationException(
                                $"Encoding '{encodingId}' is already registered " +
                                "for a different runtime DataType.");
                        }
                        continue;
                    }

                    builder ??= server.Factory.Builder;
                    builder.AddEncodeableType(
                        expandedEncodingId,
                        encodeableType);
                }
            }
            builder?.Commit();
        }

        private static ArrayOf<SemanticChangeStructureDataType> GetSemanticChanges(
            IAsyncNodeManager current,
            IAsyncNodeManager replacement)
        {
            if (current is not RuntimeNodeSetNodeManager currentRuntime ||
                replacement is not RuntimeNodeSetNodeManager replacementRuntime)
            {
                return [];
            }

            IReadOnlyDictionary<
                NodeId,
                IReadOnlyDictionary<QualifiedName, Variant>> currentProperties =
                currentRuntime.GetSemanticProperties();
            IReadOnlyDictionary<
                NodeId,
                IReadOnlyDictionary<QualifiedName, Variant>> replacementProperties =
                replacementRuntime.GetSemanticProperties();
            var changes = new List<SemanticChangeStructureDataType>();

            foreach (KeyValuePair<
                NodeId,
                IReadOnlyDictionary<QualifiedName, Variant>> entry in replacementProperties)
            {
                if (!currentProperties.TryGetValue(
                    entry.Key,
                    out IReadOnlyDictionary<QualifiedName, Variant>? previous) ||
                    !SemanticPropertiesEqual(previous, entry.Value))
                {
                    changes.Add(new SemanticChangeStructureDataType
                    {
                        Affected = entry.Key,
                        AffectedType = NodeId.Null
                    });
                }
            }

            foreach (NodeId nodeId in currentProperties.Keys)
            {
                if (!replacementProperties.ContainsKey(nodeId))
                {
                    changes.Add(new SemanticChangeStructureDataType
                    {
                        Affected = nodeId,
                        AffectedType = NodeId.Null
                    });
                }
            }

            return new ArrayOf<SemanticChangeStructureDataType>(changes.ToArray());
        }

        private static bool SemanticPropertiesEqual(
            IReadOnlyDictionary<QualifiedName, Variant> left,
            IReadOnlyDictionary<QualifiedName, Variant> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            foreach (KeyValuePair<QualifiedName, Variant> entry in left)
            {
                if (!right.TryGetValue(entry.Key, out Variant value) ||
                    !entry.Value.Equals(value))
                {
                    return false;
                }
            }
            return true;
        }

        private static async ValueTask ReportModelChangeAsync(
            IServerInternal server,
            string operation,
            CancellationToken ct)
        {
            var modelChange = new BaseModelChangeEventState(null);
            var message = new TranslationInfo(
                "LiveNodeManagerModelChange",
                "en-US",
                $"A live NodeManager was {operation}.");
            modelChange.Initialize(
                server.DefaultSystemContext,
                null,
                EventSeverity.Low,
                new LocalizedText(message));
            modelChange.SetChildValue(
                server.DefaultSystemContext,
                BrowseNames.SourceNode,
                ObjectIds.Server,
                false);
            modelChange.SetChildValue(
                server.DefaultSystemContext,
                BrowseNames.SourceName,
                "Server",
                false);
            await server.ReportEventAsync(modelChange, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Reports semantic changes independently of successful model change delivery.
        /// </summary>
        private static async ValueTask ReportSemanticChangeAsync(
            IServerInternal server,
            ArrayOf<SemanticChangeStructureDataType> semanticChanges,
            CancellationToken ct)
        {
            if (semanticChanges.Count > 0)
            {
                var semanticChange = new SemanticChangeEventState(null);
                semanticChange.Initialize(
                    server.DefaultSystemContext,
                    null,
                    EventSeverity.Low,
                    new LocalizedText(
                        "Runtime NodeSet semantic properties changed."));
                semanticChange.SetChildValue(
                    server.DefaultSystemContext,
                    BrowseNames.SourceNode,
                    ObjectIds.Server,
                    false);
                semanticChange.SetChildValue(
                    server.DefaultSystemContext,
                    BrowseNames.SourceName,
                    "Server",
                    false);
                semanticChange.CreateOrReplaceChanges(
                    server.DefaultSystemContext,
                    null);
                semanticChange.Changes!.Value = semanticChanges;
                await server.ReportEventAsync(semanticChange, ct).ConfigureAwait(false);
            }
        }

        private static async ValueTask NotifyNamespaceTableChangedAsync(
            IServerInternal server,
            int namespaceCountBefore,
            GenerationReconciler.ReconciliationWork reconciliation,
            NodeManagerGeneration[] generations)
        {
            ServerObjectState serverObject =
                server.DiagnosticsNodeManager.FindPredefinedNode<ServerObjectState>(
                    ObjectIds.Server);
            if (server.NamespaceUris.Count != namespaceCountBefore)
            {
                serverObject.NamespaceArray?.UpdateChangeMasks(NodeStateChangeMasks.Value);
                if (serverObject.NamespaceArray is not null)
                {
                    await reconciliation.RunAsync(
                        NodeManagerReconciliationStage.Namespaces,
                        generations,
                        () => serverObject.NamespaceArray.ClearChangeMasksAsync(
                            server.DefaultSystemContext,
                            includeChildren: false,
                            CancellationToken.None)).ConfigureAwait(false);
                }
            }

            // raised from the table versions, so a publication that appends only server
            // uris raises it as well and a reload of the same namespaces does not.
            PropertyState<uint>? urisVersion = ServerUrisVersion.Advance(server);
            if (urisVersion is not null)
            {
                await reconciliation.RunAsync(
                    NodeManagerReconciliationStage.Namespaces,
                    generations,
                    () => urisVersion.ClearChangeMasksAsync(
                        server.DefaultSystemContext,
                        includeChildren: false,
                        CancellationToken.None)).ConfigureAwait(false);
            }
        }

        private async ValueTask DisposeNodeManagerAsync(
            PrivateBatchCleanup cleanup,
            PrivateCandidateCleanup candidate)
        {
            if (!candidate.Detached)
            {
                lock (m_privateCleanupLock)
                {
                    cleanup.Stage = PrivateCleanupStage.Disposal;
                }
                ChangePreparer.Detach(candidate.Manager);
                candidate.Detached = true;
            }
            CheckPrivateCleanupFailures(cleanup, candidate);
            if (!candidate.ManagerDisposed)
            {
                lock (m_privateCleanupLock)
                {
                    cleanup.Stage = PrivateCleanupStage.Disposal;
                }
                await NodeManagerCleanupRecovery.DisposeManagerAsync(
                    candidate.Manager,
                    candidate.Generation,
                    () =>
                    {
                        lock (m_privateCleanupLock)
                        {
                            candidate.ManagerDisposed = true;
                            cleanup.Stage = PrivateCleanupStage.CompletedStage;
                        }
                    }).ConfigureAwait(false);
            }
            CheckPrivateCleanupFailures(cleanup, candidate);
            if (!candidate.StagingDisposed)
            {
                lock (m_privateCleanupLock)
                {
                    cleanup.Stage = PrivateCleanupStage.StagingDisposal;
                }
                await ChangePreparer.DisposeAsync(candidate.Manager).ConfigureAwait(false);
                candidate.StagingDisposed = true;
            }
            CheckPrivateCleanupFailures(cleanup, candidate);
        }

        private void CheckPrivateCleanupFailures(
            PrivateBatchCleanup cleanup,
            PrivateCandidateCleanup candidate)
        {
            lock (m_privateCleanupLock)
            {
                cleanup.Stage = PrivateCleanupStage.CompletedStage;
            }
            NodeManagerCleanupRecovery.EnsureNoRetainedFailures(candidate.Generation);
        }

        /// <summary>
        /// Tracks monitored items while a NodeManager reload detaches them from the retiring
        /// NodeManager and attaches compatible items to the replacement.
        /// </summary>
        internal sealed class MonitoredItemTransition
        {
            private readonly Func<CancellationToken, ValueTask>[] m_sourceMonitoredNodeDrains;

            /// <summary>
            /// Creates a monitored item transition with the item sets selected during reload
            /// preparation.
            /// </summary>
            /// <param name="current">The retiring source, or null only for an empty transition.</param>
            /// <param name="replacement">The lifecycle endpoint for the replacement NodeManager, if any.</param>
            /// <param name="compatibleItems">Items that can be handed to the replacement NodeManager.</param>
            /// <param name="deletedItems">Items whose nodes disappeared during reload.</param>
            /// <param name="isOwnedBySubscription">Optional test hook that verifies subscription ownership.</param>
            /// <param name="terminal">Whether lost items require client-side recreation.</param>
            public MonitoredItemTransition(
                INodeManagerMonitoredItemSource? current,
                INodeManagerMonitoredItemSource? replacement,
                IReadOnlyList<IMonitoredItem> compatibleItems,
                IReadOnlyList<IMonitoredItem> deletedItems,
                Func<IMonitoredItem, bool>? isOwnedBySubscription = null,
                bool terminal = false)
            {
                if (current is null && (compatibleItems.Count != 0 || deletedItems.Count != 0))
                {
                    throw new ArgumentNullException(nameof(current));
                }
                m_current = current;
                m_replacement = replacement;
                m_compatibleItems = compatibleItems;
                m_deletedItems = deletedItems;
                m_isOwnedBySubscription = isOwnedBySubscription;
                m_terminal = terminal;
                var sourceMonitoredNodeDrains = new List<Func<CancellationToken, ValueTask>>();
                var sourceNodeIds = new HashSet<NodeId>();
                foreach (IMonitoredItem monitoredItem in compatibleItems.Concat(deletedItems))
                {
                    if (!sourceNodeIds.Add(monitoredItem.NodeId))
                    {
                        continue;
                    }
                    if (current!.GetNotificationDrain(monitoredItem.NodeId) is { } drain)
                    {
                        sourceMonitoredNodeDrains.Add(drain);
                    }
                }
                m_sourceMonitoredNodeDrains = [.. sourceMonitoredNodeDrains];
            }

            /// <summary>
            /// Waits for admitted notifications on the monitored nodes whose items are transitioning.
            /// </summary>
            /// <param name="ct">Cancels the wait.</param>
            /// <returns>A task that completes when the captured node consumers have drained.</returns>
            public async ValueTask WaitForPendingNotificationsAsync(CancellationToken ct)
            {
                if (m_sourceNotificationsDrained)
                {
                    return;
                }
                foreach (Func<CancellationToken, ValueTask> drain in m_sourceMonitoredNodeDrains)
                {
                    await drain(ct).ConfigureAwait(false);
                }
                m_sourceNotificationsDrained = true;
            }

            /// <summary>
            /// Releases and adopts each subscribed item before moving to the next item.
            /// Incompatible items are released for invalidation after admitted notifications drain.
            /// </summary>
            /// <param name="ct">The token that aborts handoff work.</param>
            /// <returns>The non-fatal failures that occurred while releasing or adopting individual items.</returns>
            public async ValueTask<List<Exception>> AdoptAsync(
                CancellationToken ct)
            {
                var failures = new List<Exception>();
                for (int index = 0; index < m_compatibleItems.Count + m_deletedItems.Count; index++)
                {
                    bool compatible = index < m_compatibleItems.Count;
                    IMonitoredItem monitoredItem = compatible
                        ? m_compatibleItems[index]
                        : m_deletedItems[index - m_compatibleItems.Count];
                    if (m_reconciledItems.ContainsKey(monitoredItem) || !IsOwnedBySubscription(monitoredItem))
                    {
                        continue;
                    }

                    var binding = monitoredItem as IMonitoredItemGenerationBinding;
                    if (binding is { IsRemoved: true } or { IsTerminal: true })
                    {
                        continue;
                    }

                    bool attached = false;
                    MonitoredItemSourceSnapshot source = binding?.CaptureSource() ?? default;
                    long sourceRevision = source.Revision;
                    try
                    {
                        ServiceResult result = await m_current!.ReleaseSourceAsync(
                            monitoredItem,
                            sourceRevision,
                            ct)
                            .ConfigureAwait(false);
                        if (ServiceResult.IsBad(result))
                        {
                            if (!IsOwnedBySubscription(monitoredItem))
                            {
                                continue;
                            }
                            throw new ServiceResultException(result);
                        }
                        if (!compatible ||
                            m_replacement is null ||
                            binding?.IsRemoved == true ||
                            !IsOwnedBySubscription(monitoredItem))
                        {
                            m_reconciledItems[monitoredItem] = sourceRevision;
                            continue;
                        }
                        var adoption = new MonitoredItemAdoption(sourceRevision);
                        try
                        {
                            result = await m_replacement.AdoptSourceAsync(
                                monitoredItem,
                                adoption,
                                ct).ConfigureAwait(false);
                            if (ServiceResult.IsGood(result))
                            {
                                attached = true;
                            }
                            else
                            {
                                MarkAttachFailure(monitoredItem);
                                if (!IsExpectedMonitoredItemIncompatibility(result))
                                {
                                    failures.Add(new ServiceResultException(result));
                                }
                            }
                        }
                        catch (ServiceResultException ex) when (
                            binding?.IsRemoved == true && ex.StatusCode == StatusCodes.BadMonitoredItemIdInvalid)
                        {
                            // Native rollback owns cleanup; preserve any registration it retains for retry.
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            MarkAttachFailure(monitoredItem);
                            failures.Add(ex);
                        }
                        finally
                        {
                            sourceRevision = adoption.SourceRevision;
                        }
                        m_reconciledItems[monitoredItem] = sourceRevision;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        if (m_terminal &&
                            source.Generation is { } generation &&
                            binding?.TryInvalidateRetiredSource(sourceRevision, generation) == true)
                        {
                            m_reconciledItems[monitoredItem] = sourceRevision;
                        }
                        failures.Add(ex);
                    }
                    finally
                    {
                        if (attached && (binding?.IsRemoved == true || !IsOwnedBySubscription(monitoredItem)))
                        {
                            // Client deletion during handoff must also stop replacement sampling.
                            try
                            {
                                ServiceResult result = await m_replacement!.ReleaseSourceAsync(
                                    monitoredItem,
                                    sourceRevision,
                                    ct).ConfigureAwait(false);
                                if (ServiceResult.IsBad(result))
                                {
                                    failures.Add(new ServiceResultException(result));
                                }
                            }
                            catch (Exception ex) when (ex is not OutOfMemoryException)
                            {
                                failures.Add(ex);
                            }
                        }
                    }
                }

                return failures;
            }

            /// <summary>
            /// Marks deleted or incompatible items as removed after the replacement transition
            /// succeeds.
            /// </summary>
            public void MarkDeletedItems()
            {
                var failures = new List<Exception>();
                foreach (IMonitoredItem monitoredItem in m_deletedItems.Concat(m_failedItems))
                {
                    if (!m_reconciledItems.TryGetValue(monitoredItem, out long sourceRevision) ||
                        m_markedDeletedItems.Contains(monitoredItem) ||
                        !IsOwnedBySubscription(monitoredItem) ||
                        monitoredItem is IMonitoredItemGenerationBinding { IsRemoved: true })
                    {
                        continue;
                    }

                    try
                    {
                        ((IMonitoredItemGenerationBinding)monitoredItem).TryInvalidateSource(
                            sourceRevision, m_terminal);
                        m_markedDeletedItems.Add(monitoredItem);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        failures.Add(failure);
                    }
                }
                if (failures.Count > 0)
                {
                    throw new AggregateException(
                        "One or more invalidated monitored items could not be marked deleted.",
                        failures);
                }
            }

            private void MarkAttachFailure(IMonitoredItem monitoredItem)
            {
                m_failedItems.Add(monitoredItem);
            }

            private bool IsOwnedBySubscription(IMonitoredItem monitoredItem)
            {
                if (m_isOwnedBySubscription is not null)
                {
                    return m_isOwnedBySubscription(monitoredItem);
                }

                return monitoredItem.SubscriptionCallback is ISubscriptionMonitoredItemLifecycle lifecycle &&
                    lifecycle.ContainsMonitoredItem(monitoredItem);
            }

            /// <summary>The source owner, absent only for an empty transition.</summary>
            private readonly INodeManagerMonitoredItemSource? m_current;
            private readonly INodeManagerMonitoredItemSource? m_replacement;
            private readonly IReadOnlyList<IMonitoredItem> m_compatibleItems;
            private readonly IReadOnlyList<IMonitoredItem> m_deletedItems;
            private readonly Func<IMonitoredItem, bool>? m_isOwnedBySubscription;

            /// <summary>Whether items lost by this transition cannot be adopted again.</summary>
            private readonly bool m_terminal;

            /// <summary>The source incarnation released by each completed committed handoff.</summary>
            private readonly Dictionary<IMonitoredItem, long> m_reconciledItems =
                new(MonitoredItemReferenceComparer.Instance);

            private readonly List<IMonitoredItem> m_failedItems = [];
            private readonly HashSet<IMonitoredItem> m_markedDeletedItems = [];

            /// <summary>Whether the source consumers have already completed this transition's fences.</summary>
            private bool m_sourceNotificationsDrained;
        }

        private sealed class RegistrationState
        {
            public RegistrationState(
                NodeManagerRegistration registration,
                PreparedNodeManager prepared)
            {
                Registration = registration;
                Prepared = prepared;
            }

            public NodeManagerRegistration Registration { get; }

            public PreparedNodeManager Prepared { get; }

            /// <summary>
            /// Gets or sets whether the readiness callback of the registration has not yet completed.
            /// </summary>
            public bool ReadinessPending
            {
                get => Registration.Identity.ReadinessPending;
                set => Registration.Identity.ReadinessPending = value;
            }

            /// <summary>
            /// Gets or sets whether the lifecycle operation currently owns readiness completion.
            /// </summary>
            public bool ReadinessInProgress { get; set; }

            /// <summary>
            /// Gets or sets whether this generation published staged historian registrations.
            /// </summary>
            public bool HistorianRegistrationsPublished { get; set; }

            /// <summary>
            /// Gets or sets the shared producer-stop attempt for this registration.
            /// </summary>
            public Task? ShutdownPreparation { get; set; }

            /// <summary>
            /// Gets whether this registration supplies a protected system role.
            /// </summary>
            public bool IsSystem { get; init; }
        }

        /// <inheritdoc/>
        public ValueTask<IPreparedChanges> PrepareAsync(
            ArrayOf<LifecycleChange> changes,
            CancellationToken cancellationToken = default)
        {
            if (changes.Count == 0)
            {
                throw new ArgumentException("A publication unit must contain at least one change.", nameof(changes));
            }
            return PrepareChangesAsync(changes, null, CaptureLifecycleCallbackPermit(), cancellationToken);
        }

        /// <summary>
        /// Validates a publication unit and prepares its node managers privately, without publishing them.
        /// </summary>
        /// <param name="changes">The additions, replacements and retirements of the unit.</param>
        /// <param name="publication">The invocation that owns publication admission, or null for a standalone
        /// batch.</param>
        /// <param name="callbackPermit">The callback permit captured when preparation began, if any.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <param name="readImages">Read images to publish together with the changes.</param>
        /// <param name="validationOnly">Whether the batch is a validation candidate that can never be
        /// committed.</param>
        /// <param name="initial">The explicit startup capability, or null for runtime preparation.</param>
        /// <param name="shutdown">The explicit removal-only shutdown capability, or null.</param>
        /// <returns>The prepared batch, which must be disposed if it is not committed.</returns>
        /// <exception cref="NotSupportedException">
        /// The server factory does not support prepared publication,
        /// or a manager being replaced does not support safe reload.
        /// </exception>
        private async ValueTask<IPreparedChanges> PrepareChangesAsync(
            ArrayOf<LifecycleChange> changes,
            LifecycleWriter.PublicationSession? publication,
            NodeManagerCallbackPermit? callbackPermit,
            CancellationToken cancellationToken,
            ArrayOf<INodeManagerReadImage> readImages = default,
            bool validationOnly = false,
            InitialPublication? initial = null,
            ShutdownPublication? shutdown = null)
        {
            if (changes.IsNull || (changes.Count == 0 && readImages.Count == 0 && initial is null))
            {
                throw new ArgumentException("A publication unit must contain at least one change.", nameof(changes));
            }
            changes = [.. changes];
            readImages = [.. readImages];
            var identities = new HashSet<Guid>();
            foreach (LifecycleChange change in changes)
            {
                if (change is null)
                {
                    throw new ArgumentException("A publication change cannot be null.", nameof(changes));
                }
                Guid? targetId = change.Slot?.Id;
                if (targetId is { } id && !identities.Add(id))
                {
                    throw new ArgumentException(
                        "A slot occurs twice in the same unit.",
                        nameof(changes));
                }
            }
            if (shutdown is not null)
            {
                if (initial is not null || readImages.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Shutdown publication cannot include initialization or read images.");
                }
                ShutdownPublication.ValidateChanges(changes);
            }

            var entries = new List<BatchEntry>();
            OperationLifetime? operation = null;
            try
            {
                operation = shutdown?.EnterOperation() ?? EnterLifecycleOperation();
                (IServerInternal server, IDynamicNodeManagerHost host) =
                    shutdown is not null ? shutdown.GetServer() :
                    initial is null ? GetRunningServer() : initial.GetServer();
                if (server.Factory is not EncodeableFactory)
                {
                    throw new NotSupportedException("The server factory does not support prepared publication.");
                }
                m_reclaimer.RetryReady();
                LifecycleWriter.Mutation mutation = await m_writer.BeginMutationAsync(cancellationToken)
                    .ConfigureAwait(false);
                var admissions = new List<(NodeManagerGeneration Generation, int Stripe)>(changes.Count);
                try
                {
                    EnsurePublicationServer(server, host, initial, shutdown);
                    publication?.ValidateRevision(server);
                    var preparation = new ChangePreparer(server);
                    NodeManagerRoutingTable.RoutingSnapshot routingRevision = host.RoutingRevision;
                    IDataTypeDefinitionResolver? resolver = null;
                    int namespaceCount = server.NamespaceUris.Count;
                    for (int index = 0; index < changes.Count; index++)
                    {
                        LifecycleChange change = changes[index];
                        NodeManagerRegistration? target = null;
                        if (change.Slot is { } slot)
                        {
                            lock (m_registrationLock)
                            {
                                if (!m_registrations.TryGetValue(slot.Id, out RegistrationState? state) ||
                                    !ReferenceEquals(state.Registration.Slot, slot))
                                {
                                    throw new InvalidOperationException(
                                        "The target slot is not owned by this lifecycle.");
                                }
                                target = state.Registration;
                            }
                        }
                        RegistrationState? current = target is null
                            ? null
                            : GetCurrentState(
                                target,
                                shutdown is not null,
                                allowRemovalAfterFailedReadiness: change.Factory is null);
                        if (current?.IsSystem == true && shutdown is null)
                        {
                            throw new InvalidOperationException(
                                "System slots can only be withdrawn by server shutdown.");
                        }
                        if (change.Factory is { } slotFactory)
                        {
                            ValidateFactoryRegistration(slotFactory, target);
                        }
                        if (current is not null)
                        {
                            NodeManagerGeneration generation = current.Registration.Identity;
                            if (!generation.Gate.TryEnter(out int stripe))
                            {
                                throw new InvalidOperationException(
                                    "A registration retired before its preparation could be admitted.");
                            }
                            admissions.Add((generation, stripe));
                        }
                        entries.Add(new BatchEntry(change, current));
                    }
                    mutation.Dispose();
                    foreach (BatchEntry entry in entries)
                    {
                        LifecycleChange change = entry.Change;
                        RegistrationState? current = entry.Current;
                        cancellationToken.ThrowIfCancellationRequested();
                        if (change.Factory is null)
                        {
                            continue;
                        }
                        entry.Manager = await preparation.CreateNodeManagerAsync(
                            change.Factory,
                            m_server.CurrentConfiguration,
                            current is not null,
                            IsOwnedNodeManager,
                            cancellationToken).ConfigureAwait(false);
                        ChangePreparer.StagingServer stagingServer = ChangePreparer.GetServer(entry.Manager)!;
                        try
                        {
                            ChangePreparer.ValidateHistorianReadiness(entry.Manager);
                            stagingServer.Attach(entry.Manager);
                            NodeManagerGeneration generation = current is null
                                ? NodeManagerGenerationRegistry.CreateAddition(entry.Manager)
                                : NodeManagerGenerationRegistry.CreateReplacement(
                                    current.Registration.Identity, entry.Manager);
                            entry.Prepared = await host.PrepareAsync(
                                entry.Manager, generation, current?.Prepared.NodeManager, cancellationToken)
                                .ConfigureAwait(false);
                            if (entry.Prepared.PreparationFailure is { } preparationFailure)
                            {
                                entry.Prepared.PreparationFailure = null;
                                ExceptionDispatchInfo.Capture(preparationFailure).Throw();
                            }
                            await ValidateDataTypeCompatibilityAsync(stagingServer, entry.Manager, cancellationToken)
                                .ConfigureAwait(false);
                            if (current is not null)
                            {
                                if (current.Prepared.NodeManager is not INodeManagerReloadParticipant participant)
                                {
                                    throw new NotSupportedException(
                                        "A replacement does not support safe reload.");
                                }
                                entry.DroppedReferences =
                                [
                                    .. await participant.PrepareReloadAsync(entry.Manager, cancellationToken)
                                        .ConfigureAwait(false)
                                ];
                            }
                            resolver = await m_server.PrepareComplexTypesAsync(
                                stagingServer, entry.Manager, cancellationToken)
                                .ConfigureAwait(false) ??
                                resolver;
                            var registration = new NodeManagerRegistration(
                                current?.Registration.Id ?? Guid.NewGuid(),
                                (current?.Registration.Generation ?? 0) + 1,
                                entry.Manager);
                            entry.Next = new RegistrationState(registration, entry.Prepared)
                            {
                                ReadinessPending = NodeManagerReadiness.IsParticipant(entry.Manager)
                            };
                        }
                        finally
                        {
                            stagingServer?.Dispose();
                        }
                    }
                    if (server is INodeIdFactoryProvider
                        { NodeIdFactory: INodeIdFactoryPolicy policy })
                    {
                        foreach (BatchEntry entry in entries)
                        {
                            if (entry.Manager is not null)
                            {
                                await policy.PrepareNodeManagerAsync(
                                    ChangePreparer.GetServer(entry.Manager)!,
                                    entry.Manager,
                                    entry.Current?.Prepared.NodeManager,
                                    cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                    var payload = new PreparationPayload(
                        this, server, host, entries, operation, namespaceCount, routingRevision,
                        preparation.TypeTree, preparation.OriginalTypes,
                        preparation.TypeRevision, preparation.Factory, preparation.OriginalFactory,
                        preparation.FactoryRevision, resolver, preparation, publication,
                        initial, shutdown, callbackPermit);
                    var prepared = new PreparedChanges(payload, validationOnly);
                    if (readImages.Count != 0)
                    {
                        foreach (INodeManagerReadImage image in readImages)
                        {
                            prepared.SetMetadata(image);
                        }
                    }
                    operation = null;
                    return prepared;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    mutation.Dispose();
                    ReleaseAdmissions();
                    Exception? cleanup = await AbortBatchEntriesAsync(server, host, entries)
                        .ConfigureAwait(false);
                    if (cleanup is not null)
                    {
                        throw new AggregateException("Batch preparation and abort both failed.", failure, cleanup);
                    }
                    throw;
                }
                finally
                {
                    mutation.Dispose();
                    ReleaseAdmissions();
                }

                void ReleaseAdmissions()
                {
                    foreach ((NodeManagerGeneration generation, int stripe) in admissions)
                    {
                        generation.Gate.Exit(stripe);
                    }
                    admissions.Clear();
                }
            }
            finally
            {
                operation?.Dispose();
            }
        }

        /// <summary>
        /// Validates or commits a prepared batch: switches routing to the prepared image, retires replaced generations,
        /// and reports cleanup failures after the commit.
        /// </summary>
        /// <param name="batch">The prepared batch to complete.</param>
        /// <param name="decideAsync">The durable decision callback, or null for a validation candidate.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <param name="publishCommittedState">The callback invoked after the committed image is installed, or
        /// null.</param>
        /// <returns>The committed result, or null for a validation candidate.</returns>
        private async ValueTask<LifecycleResult?> CompleteChangesAsync(
            PreparationPayload batch,
            Func<CancellationToken, ValueTask>? decideAsync,
            CancellationToken cancellationToken,
            Action? publishCommittedState = null)
        {
            ThrowIfDecisionCallbackActive();

            LifecycleWriter.Mutation mutation =
                await m_writer.BeginMutationAsync(cancellationToken).ConfigureAwait(false);
            var retired = new List<RetiredGeneration>();
            bool releaseWriter = true;
            var failures = new List<Exception>();
            var monitoredItemTransitions = new List<MonitoredItemTransition>();
            var semanticChanges = new List<SemanticChangeStructureDataType>();
            var preparationAdmissions = new List<(NodeManagerGeneration Generation, int Stripe)>(batch.Entries.Count);
            List<RegistrationState>? readinessClaims = null;
            LifecycleCommit? committedLifecycle = null;
            NodeManagerSlot[] publishedSlots = [];
            ArrayOf<NodeManagerSlotSnapshot> publishedSlotSnapshots = [];
            GenerationReconciler reconciler = GetGenerationReconciler(batch.Server);
            GenerationReconciler.ReconciliationWork reconciliation =
                reconciler.CreateReconciliation(() => EnterLifecycleOperation(), () => m_writer.IsShuttingDown);
            try
            {
                EnsurePublicationServer(batch.Server, batch.Host, batch.Initial, batch.Shutdown);
                foreach (BatchEntry entry in batch.Entries)
                {
                    if (entry.Next is { ReadinessPending: true } next)
                    {
                        (readinessClaims ??= []).Add(next);
                    }
                }
                if (batch.Initial is { } systemPublication)
                {
                    foreach (NodeManagerRegistration registration in systemPublication.SystemRegistrations)
                    {
                        RegistrationState state = GetCurrentState(registration, allowPendingReadiness: true);
                        if (state.ReadinessPending)
                        {
                            (readinessClaims ??= []).Add(state);
                        }
                    }
                }
                if (!ReferenceEquals(
                    batch.Host.RoutingRevision, batch.RoutingRevision))
                {
                    throw new NodeManagerRoutingConflictException(
                        "The address space changed while the batch was prepared.", batch.RoutingRevision);
                }
                foreach (BatchEntry entry in batch.Entries)
                {
                    if (entry.Current is not null &&
                        !ReferenceEquals(
                            GetCurrentState(
                                entry.Current.Registration,
                                batch.Shutdown is not null,
                                allowRemovalAfterFailedReadiness: entry.Change.Factory is null),
                            entry.Current))
                    {
                        throw new InvalidOperationException("A registration changed while the batch was prepared.");
                    }
                    if (entry.Current is not null)
                    {
                        NodeManagerGeneration generation = entry.Current.Registration.Identity;
                        if (!generation.Gate.TryEnter(out int stripe))
                        {
                            throw new InvalidOperationException(
                                "A registration retired before its transition preparation could be admitted.");
                        }
                        preparationAdmissions.Add((generation, stripe));
                    }
                }
                mutation.Dispose();
                foreach (BatchEntry entry in batch.Entries)
                {
                    if (entry.Current is not null)
                    {
                        if (decideAsync is not null && entry.Manager is not null)
                        {
                            semanticChanges.AddRange(GetSemanticChanges(
                                entry.Current.Prepared.NodeManager,
                                entry.Manager));
                        }
                        var retirement = new RetiredGeneration(
                            batch.Server,
                            batch.Host,
                            entry.Current.Registration.Identity,
                            entry.DroppedReferences,
                            needsDetachment: true,
                            allowActiveMonitoredItems: false);
                        retired.Add(retirement);
                        if (entry.Change.TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate &&
                            entry.Manager is not null)
                        {
                            retirement.MigrateLateMonitoredItemsAsync =
                                () => MigrateLateMonitoredItemsAsync(retirement);
                        }
                        if (decideAsync is not null)
                        {
                            MonitoredItemTransition transition = await PrepareMonitoredItemTransitionAsync(
                                entry.Current.Prepared.NodeManager,
                                entry.Change.TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate
                                    ? entry.Manager
                                    : null,
                                cancellationToken).ConfigureAwait(false);
                            monitoredItemTransitions.Add(transition);
                            retired[^1].Transition = transition;
                        }
                    }
                }

                ArrayOf<PreparedNodeManager> candidates = batch.Entries
                    .Where(entry => entry.Prepared is not null)
                    .Select(entry => entry.Prepared!)
                    .ToArrayOf();
                ArrayOf<IAsyncNodeManager> removed = batch.Entries
                    .Where(entry => entry.Current is not null && entry.Prepared is null)
                    .Select(entry => entry.Current!.Prepared.NodeManager)
                    .ToArrayOf();
                using PreparedAddressSpaceReferences references =
                    await batch.Host.PrepareReferencesAsync(
                        candidates,
                        removed,
                        batch.RoutingRevision,
                        cancellationToken,
                        batch.CallbackPermit).ConfigureAwait(false);
                foreach (KeyValuePair<IAsyncNodeManager, List<LocalReference>> deferred in references.DeferredRemovals)
                {
                    RetiredGeneration? retirement = retired.FirstOrDefault(item =>
                        ReferenceEquals(item.NodeManager, deferred.Key));
                    if (retirement is null)
                    {
                        throw new InvalidOperationException(
                            "A deferred reference removal has no retiring NodeManager.");
                    }
                    retirement.PendingReferences.AddRange(deferred.Value);
                }

                using INodeManagerBatchAdmission admission =
                    await batch.Host.AcquireCommitAdmissionAsync(
                        batch.CallbackPermit, cancellationToken).ConfigureAwait(false);
                mutation = await m_writer.BeginMutationAsync(cancellationToken).ConfigureAwait(false);
                EnsurePublicationServer(batch.Server, batch.Host, batch.Initial, batch.Shutdown);
                if (!ReferenceEquals(
                    batch.Host.RoutingRevision, batch.RoutingRevision))
                {
                    throw new NodeManagerRoutingConflictException(
                        "The address space changed while transitions were prepared.", batch.RoutingRevision);
                }
                foreach ((NodeManagerGeneration generation, int stripe) in preparationAdmissions)
                {
                    generation.Gate.Exit(stripe);
                }
                preparationAdmissions.Clear();

                Dictionary<Guid, RegistrationState> committedRegistrations;
                lock (m_registrationLock)
                {
                    foreach (BatchEntry entry in batch.Entries)
                    {
                        if (entry.Current is not null &&
                            (!m_registrations.TryGetValue(
                                entry.Current.Registration.Id,
                                out RegistrationState? current) ||
                                !ReferenceEquals(current, entry.Current)))
                        {
                            throw new InvalidOperationException(
                                "A registration changed while the batch was prepared.");
                        }
                    }

                    committedRegistrations = new Dictionary<Guid, RegistrationState>(m_registrations);
                    foreach (BatchEntry entry in batch.Entries)
                    {
                        if (entry.Current is not null)
                        {
                            committedRegistrations.Remove(entry.Current.Registration.Id);
                        }
                        if (entry.Next is not null)
                        {
                            committedRegistrations[entry.Next.Registration.Id] = entry.Next;
                        }
                    }
                    publishedSlots = committedRegistrations.Values
                        .Where(state => !state.IsSystem)
                        .Select(state => state.Registration.Slot)
                        .ToArray();
                    publishedSlotSnapshots = committedRegistrations.Values
                        .Where(state => !state.IsSystem)
                        .Select(state => new NodeManagerSlotSnapshot(
                            state.Registration.Slot, state.Registration.Generation, state.Registration.NodeManager))
                        .ToArrayOf();
                    m_reclaimer.Reserve(retired.Count);
                }

                NodeManagerGeneration[] candidateGenerations =
                [
                    .. (batch.Initial?.SystemRegistrations.ToArray() ?? [])
                        .Select(registration => registration.Identity)
                        .Concat((candidates.ToArray() ?? []).Select(candidate => candidate.Generation))
                ];
                var lifecycleCommit = new LifecycleCommit(
                    this, batch, mutation, decideAsync, committedRegistrations, retired, readinessClaims, reconciler);
                committedLifecycle = lifecycleCommit;
                bool reconciliationStarted = false;
                async ValueTask PublishCommittedStateAsync()
                {
                    if (reconciliationStarted)
                    {
                        return;
                    }
                    reconciliationStarted = true;
                    mutation.Dispose();
                    releaseWriter = false;
                    reconciliation.Publish(lifecycleCommit.Revision);
                    if (publishCommittedState is not null)
                    {
                        await reconciliation.RunAsync(
                            NodeManagerReconciliationStage.ApplicationState,
                            candidateGenerations,
                            () =>
                            {
                                publishCommittedState();
                                return default;
                            },
                            requireOriginalView: true).ConfigureAwait(false);
                    }
                }

                PublishedReferenceChanges? committedReferences = null;
                try
                {
                    var context = new NodeManagerPublicationPlan(
                        Candidates: candidates,
                        Removed: removed,
                        TypeTree: batch.TypeTree,
                        OriginalTypes: batch.OriginalTypes,
                        TypeRevision: batch.TypeRevision,
                        Factory: batch.Factory,
                        OriginalFactory: batch.OriginalFactory,
                        FactoryRevision: batch.FactoryRevision,
                        ReadImages: batch.ReadImages,
                        Commit: lifecycleCommit,
                        CaptureCommittedReferences: committed => committedReferences = committed,
                        CancellationToken: cancellationToken,
                        Preparation: batch.Preparation,
                        References: references,
                        CallbackPermit: batch.CallbackPermit,
                        Admission: admission);
                    committedReferences = await batch.Host.CommitBatchAsync(context).ConfigureAwait(false);
                }
                catch (Exception failure) when (
                    batch.DecisionCommitted &&
                    !batch.IsCommitted &&
                    failure is not OutOfMemoryException)
                {
                    m_writer.MarkPublicationFailed();
                    _ = await AbortBatchEntriesAsync(
                        batch.Server, batch.Host, batch.Entries,
                        deferUntilShutdown: true).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        "The durable NodeManager decision succeeded, but routing publication failed before the " +
                        "serving view changed. Candidate cleanup is deferred until server shutdown; " +
                        "restart the server to recover.",
                        failure);
                }
                catch (Exception failure) when (batch.IsCommitted && failure is not OutOfMemoryException)
                {
                    reconciliation.ReportOwnedFailure(NodeManagerReconciliationStage.Publication, failure);
                }
                finally
                {
                    committedReferences ??= lifecycleCommit.CommittedReferences;
                }
                if (decideAsync is null)
                {
                    return null;
                }
                mutation.Dispose();
                releaseWriter = false;
                await PublishCommittedStateAsync().ConfigureAwait(false);
                if (batch.Initial is { } initialPublication)
                {
                    await initialPublication.WaitForRuntimeAsync().ConfigureAwait(false);
                }
                if (batch.Entries.Count != 0 &&
                    batch.Server is IConformanceUnitsManagerProvider
                    {
                        ConformanceUnitsManager: { } conformanceUnitsManager
                    })
                {
                    foreach (RetiredGeneration retirement in retired)
                    {
                        try
                        {
                            retirement.ConformanceDrain ??= conformanceUnitsManager.BeginNodeManagerCleanup(
                                retirement.NodeManager, withdraw: true);
                        }
                        catch (Exception failure) when (failure is not OutOfMemoryException)
                        {
                            reconciliation.ReportOwnedFailure(NodeManagerReconciliationStage.Publication, failure);
                        }
                    }
                    await reconciliation.RunAsync(
                        NodeManagerReconciliationStage.Publication,
                        [],
                        () => conformanceUnitsManager.PublishAsync(CancellationToken.None)).ConfigureAwait(false);
                }
                try
                {
                    if (committedReferences is not null)
                    {
                        await committedReferences.ReconcileAdditionsAsync(reconciliation).ConfigureAwait(false);
                    }
                    foreach (MonitoredItemTransition transition in monitoredItemTransitions)
                    {
                        try
                        {
                            foreach (Exception failure in await transition.AdoptAsync(
                                CancellationToken.None).ConfigureAwait(false))
                            {
                                reconciliation.ReportOwnedFailure(
                                    NodeManagerReconciliationStage.MonitoredItems, failure);
                            }
                        }
                        catch (Exception failure) when (failure is not OutOfMemoryException)
                        {
                            reconciliation.ReportOwnedFailure(NodeManagerReconciliationStage.MonitoredItems, failure);
                        }
                    }
                    m_reclaimer.FinalizeMonitoredItemTransitions(retired);
                    foreach (NodeManagerGeneration generation in candidateGenerations)
                    {
                        await reconciliation.RunAsync(
                            NodeManagerReconciliationStage.Bindings,
                            [generation],
                            async () =>
                            {
                                GenerationReconciler.Bindings bindings = await reconciler.BindAsync(
                                    generation, CancellationToken.None).ConfigureAwait(false);
                                await reconciler.ReconcileAsync(
                                    generation, bindings, CancellationToken.None).ConfigureAwait(false);
                            }).ConfigureAwait(false);
                    }
                    if (committedReferences is not null)
                    {
                        await committedReferences.NotifyObserversAsync(reconciliation).ConfigureAwait(false);
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    reconciliation.ReportOwnedFailure(NodeManagerReconciliationStage.Publication, failure);
                }

                IEnumerable<(RegistrationState State, bool RecoverSources)> readinessStates = batch.Entries
                    .Where(entry => entry.Next is not null)
                    .Select(entry => (entry.Next!,
                        entry.Change.TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate));
                if (batch.Initial is { } startup)
                {
                    readinessStates = startup.SystemRegistrations.ToList()
                        .Select(registration => (
                            GetCurrentState(registration, allowPendingReadiness: true), false))
                        .Concat(readinessStates);
                }
                bool historianRegistrationsChanged = lifecycleCommit.HistorianRegistrationsChanged;
                foreach ((RegistrationState state, bool recoverSources) in readinessStates)
                {
                    NodeManagerGeneration generation = state.Registration.Identity;
                    if (recoverSources)
                    {
                        using var readinessScope = new NodeManagerReadinessScope(
                            batch.Server,
                            state.Registration.Identity);
                        await reconciliation.RunAsync(
                            NodeManagerReconciliationStage.MonitoredItems,
                            [generation],
                            () => ResolveUnresolvedSourcesAsync(
                                batch.Server, generation.NodeManager, CancellationToken.None))
                            .ConfigureAwait(false);
                    }
                    await reconciliation.RunAsync(
                        NodeManagerReconciliationStage.Readiness,
                        [generation],
                        () => CompleteReadinessAsync(
                            batch.Server, batch.Host, state,
                            batch.Initial is not null ? cancellationToken : CancellationToken.None,
                            batch.Initial))
                        .ConfigureAwait(false);
                    historianRegistrationsChanged |= state.HistorianRegistrationsPublished;
                }
                if (historianRegistrationsChanged)
                {
                    await reconciliation.RunAsync(
                        NodeManagerReconciliationStage.HistorianCapabilities,
                        candidateGenerations,
                        () => batch.Server.DiagnosticsNodeManager.UpdateServerEventNotifierAsync(
                            CancellationToken.None)).ConfigureAwait(false);
                }
                // Publication and readiness are complete. Reclamation owns its records and callbacks independently;
                // this operation never reacquires the writer after handing them off.
                m_reclaimer.Activate(retired);
                batch.Host.SetRetiredGenerationDrainObserver(ScheduleRetiredGenerationDrainCleanup);
                ScheduleRetiredGenerationDrainCleanup();
                if (batch.Entries.Count != 0)
                {
                    string operation = batch.Entries.Count == 1
                        ? batch.Entries[0].Current is null
                            ? "added"
                            : batch.Entries[0].Next is null
                                ? "removed"
                                : "reloaded"
                        : "batch";
                    await reconciliation.RunAsync(
                        NodeManagerReconciliationStage.Identity,
                        candidateGenerations,
                        () => RebindIdentityAsync(batch.Server, CancellationToken.None)).ConfigureAwait(false);
                    await NotifyNamespaceTableChangedAsync(
                        batch.Server, batch.NamespaceCount, reconciliation, candidateGenerations).ConfigureAwait(false);
                    await reconciliation.RunAsync(
                        NodeManagerReconciliationStage.ModelChange,
                        candidateGenerations,
                        () => ReportModelChangeAsync(batch.Server, operation, CancellationToken.None))
                        .ConfigureAwait(false);
                    if (semanticChanges.Count != 0)
                    {
                        await reconciliation.RunAsync(
                            NodeManagerReconciliationStage.SemanticChange,
                            candidateGenerations,
                            () => ReportSemanticChangeAsync(
                                batch.Server, semanticChanges.ToArrayOf(), CancellationToken.None))
                            .ConfigureAwait(false);
                    }
                }
                reconciliation.Seal();
                failures.AddRange((reconciliation.Ticket.Issues.ToArray() ?? [])
                    .Select(issue => issue.CurrentFailure));
                uint completed;
                lock (m_registrationLock)
                {
                    completed = (uint)retired.Count(item => !m_reclaimer.Contains(item));
                }
                return new LifecycleResult(
                    batch.Registrations, completed,
                    failures.Count == 0 ? null : new AggregateException(
                        "The batch committed; generation reconciliation remains pending.", failures),
                    lifecycleCommit.Revision,
                    new ArrayOf<NodeManagerSlot>(publishedSlots),
                    Task.WhenAll(retired.Select(item => item.Retirement)),
                    reconciliation.Ticket,
                    (publishedSlotSnapshots.ToArray() ?? [])
                        .Select(static snapshot =>
                            new NodeManagerGenerationSnapshot(snapshot.Slot, snapshot.Generation))
                        .ToArrayOf());
            }
            finally
            {
                if (committedLifecycle?.ReadinessClaimed == true && readinessClaims is { Count: > 0 })
                {
                    lock (m_registrationLock)
                    {
                        foreach (RegistrationState state in readinessClaims)
                        {
                            state.ReadinessInProgress = false;
                        }
                    }
                }
                foreach ((NodeManagerGeneration generation, int stripe) in preparationAdmissions)
                {
                    generation.Gate.Exit(stripe);
                }
                if (releaseWriter)
                {
                    mutation.Dispose();
                }
                if (batch.IsCommitted)
                {
                    m_reclaimer.Activate(retired);
                    ScheduleRetiredGenerationDrainCleanup();
                }
            }
        }

        /// <summary>
        /// Owns the lifecycle side of a reserved publication without passing bookkeeping callbacks to the host.
        /// </summary>
        private sealed class LifecycleCommit : INodeManagerPublicationCommit
        {
            /// <summary>
            /// Captures prepared registration and retirement state before the durable decision.
            /// </summary>
            /// <param name="owner">The registration lifecycle owner.</param>
            /// <param name="batch">The prepared unit.</param>
            /// <param name="mutation">The mutation admission for this decision.</param>
            /// <param name="decideAsync">The application decision, or null for validation.</param>
            /// <param name="registrations">The next registration map.</param>
            /// <param name="retired">The prepared retirement records.</param>
            /// <param name="readinessClaims">The states requiring readiness after publication.</param>
            /// <param name="reconciler">The generation checkpoint owner.</param>
            public LifecycleCommit(
                NodeManagerLifecycle owner,
                PreparationPayload batch,
                LifecycleWriter.Mutation mutation,
                Func<CancellationToken, ValueTask>? decideAsync,
                Dictionary<Guid, RegistrationState> registrations,
                List<RetiredGeneration> retired,
                List<RegistrationState>? readinessClaims,
                GenerationReconciler reconciler)
            {
                m_owner = owner;
                m_batch = batch;
                m_mutation = mutation;
                m_decideAsync = decideAsync;
                m_registrations = registrations;
                m_retired = retired;
                m_readinessClaims = readinessClaims;
                m_reconciler = reconciler;
            }

            /// <inheritdoc/>
            public bool HasDecision => m_decideAsync is not null;

            /// <inheritdoc/>
            public bool IsInDecisionWindow => m_mutation.IsInDecisionWindow;

            /// <inheritdoc/>
            public IDisposable EnterDecisionWindow() => m_mutation.EnterDecisionWindow();

            /// <summary>
            /// Gets the revision installed at the routing swap.
            /// </summary>
            public long Revision { get; private set; }

            /// <summary>
            /// Gets whether publication claimed readiness work that must be released by completion.
            /// </summary>
            public bool ReadinessClaimed { get; private set; }

            /// <summary>
            /// Gets whether routing publication retired historian registrations.
            /// </summary>
            public bool HistorianRegistrationsChanged { get; private set; }

            /// <summary>
            /// Gets the committed reference changes retained if host reservation cleanup fails.
            /// </summary>
            public PublishedReferenceChanges? CommittedReferences { get; private set; }

            /// <inheritdoc/>
            public async ValueTask DecideAsync(CancellationToken cancellationToken)
            {
                m_batch.Publication?.ValidateRevision(m_batch.Server);
                await m_mutation.DecideAsync(m_decideAsync ??
                    throw new InvalidOperationException("A validation unit has no durable decision."),
                    cancellationToken).ConfigureAwait(false);
                m_batch.DecisionCommitted = true;
            }

            /// <inheritdoc/>
            public void Publish(bool historianRegistrationsChanged)
            {
                m_batch.Publication?.AdvanceRevision();
                lock (m_owner.m_registrationLock)
                {
                    if (m_batch.IsCommitted)
                    {
                        return;
                    }
                    m_batch.IsCommitted = true;
                    HistorianRegistrationsChanged = historianRegistrationsChanged;
                    m_owner.m_reclaimer.Enqueue(m_retired);
                    m_owner.m_registrations = m_registrations;
                    if (m_readinessClaims is { Count: > 0 })
                    {
                        foreach (RegistrationState state in m_readinessClaims)
                        {
                            state.ReadinessInProgress = true;
                        }
                        ReadinessClaimed = true;
                    }
                    foreach (BatchEntry entry in m_batch.Entries)
                    {
                        if (entry.Current is not null)
                        {
                            if (entry.Change.TransitionMode !=
                                NodeManagerMonitoredItemTransitionMode.Migrate ||
                                entry.Manager is null)
                            {
                                NodeManagerGeneration generation = entry.Current.Registration.Identity;
                                generation.Slot.LateMigrationRevision =
                                    unchecked(generation.Slot.LateMigrationRevision + 1);
                            }
                        }
                    }
                    foreach (RetiredGeneration retirement in m_retired)
                    {
                        retirement.LateMigrationRevision = retirement.Generation.Slot.LateMigrationRevision;
                    }
                    Revision = m_batch.Host.RoutingRevision.Revision;
                }
                foreach (RetiredGeneration retirement in m_retired)
                {
                    ChangePreparer.RevokeServiceLevelControl(retirement.NodeManager);
                }
                if (m_batch.Resolver is not null)
                {
                    m_owner.m_server.PublishComplexTypeResolver(m_batch.Resolver);
                }
                m_reconciler.SupersedeReconciliations();
            }

            /// <inheritdoc/>
            public void CaptureCommittedReferences(PublishedReferenceChanges references)
            {
                if (!m_batch.IsCommitted)
                {
                    throw new InvalidOperationException(
                        "Reference reconciliation requires a committed publication.");
                }
                if (CommittedReferences is not null)
                {
                    throw new InvalidOperationException(
                        "Reference reconciliation is already owned by this publication.");
                }
                CommittedReferences = references ?? throw new ArgumentNullException(nameof(references));
            }

            /// <summary>The registration owner.</summary>
            private readonly NodeManagerLifecycle m_owner;

            /// <summary>The prepared unit.</summary>
            private readonly PreparationPayload m_batch;

            /// <summary>The decision's mutation admission.</summary>
            private readonly LifecycleWriter.Mutation m_mutation;

            /// <summary>The external durable decision.</summary>
            private readonly Func<CancellationToken, ValueTask>? m_decideAsync;

            /// <summary>The prepared registration map.</summary>
            private readonly Dictionary<Guid, RegistrationState> m_registrations;

            /// <summary>The prepared retirement records.</summary>
            private readonly List<RetiredGeneration> m_retired;

            /// <summary>The publication's readiness claims.</summary>
            private readonly List<RegistrationState>? m_readinessClaims;

            /// <summary>The generation checkpoint owner.</summary>
            private readonly GenerationReconciler m_reconciler;
        }

        /// <summary>
        /// Rejects lifecycle re-entry from the durable decision callback.
        /// </summary>
        private void ThrowIfDecisionCallbackActive()
        {
            m_writer.ThrowIfDecisionCallbackActive();
        }

        /// <summary>
        /// Retains and aborts private candidates in reverse preparation order.
        /// </summary>
        /// <param name="server">The server owning explicit conformance registrations.</param>
        /// <param name="host">The dynamic node manager host.</param>
        /// <param name="entries">The batch entries to abort.</param>
        /// <param name="deferUntilShutdown">Retains decided candidates without aborting them before shutdown.</param>
        /// <returns>The aggregated cleanup failure, or null if the cleanup succeeded.</returns>
        private async ValueTask<Exception?> AbortBatchEntriesAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            List<BatchEntry> entries,
            bool deferUntilShutdown = false)
        {
            var candidates = new List<PrivateCandidateCleanup>();
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                BatchEntry entry = entries[index];
                if (entry.TakePrivateManager() is { } manager)
                {
                    NodeManagerGeneration generation = entry.Prepared?.Generation ??
                        (NodeManagerGenerationRegistry.TryGet(manager, out NodeManagerGeneration? reserved)
                            ? reserved!
                            : NodeManagerGenerationRegistry.CreateAddition(manager));
                    candidates.Add(new PrivateCandidateCleanup(manager, entry.Prepared, generation));
                }
            }
            if (candidates.Count == 0)
            {
                return null;
            }

            var cleanup = new PrivateBatchCleanup(host, candidates);
            lock (m_privateCleanupLock)
            {
                if (deferUntilShutdown)
                {
                    cleanup.Stage = PrivateCleanupStage.WaitingForShutdown;
                }
                m_privateCleanups.Add(cleanup);
            }
            return deferUntilShutdown
                ? null
                : await StartPrivateBatchCleanupAsync(server, cleanup).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts owned private cleanup after its publication or shutdown policy permits destruction.
        /// </summary>
        private async ValueTask<Exception?> StartPrivateBatchCleanupAsync(
            IServerInternal server,
            PrivateBatchCleanup cleanup)
        {
            Task completion = cleanup.Completion.Task;
            Task drain;
            try
            {
                foreach (PrivateCandidateCleanup candidate in cleanup.Candidates)
                {
                    if (candidate.Generation.Gate.State == NodeManagerGenerationState.Prepared)
                    {
                        candidate.Generation.Gate.Abort();
                    }
                }
                if (server is IConformanceUnitsManagerProvider { ConformanceUnitsManager: { } conformance })
                {
                    var drains = new List<Task>(cleanup.Candidates.Count);
                    foreach (PrivateCandidateCleanup candidate in cleanup.Candidates)
                    {
                        drains.Add(conformance.BeginNodeManagerCleanup(candidate.Manager, withdraw: false));
                    }
                    drain = Task.WhenAll(drains);
                }
                else
                {
                    drain = Task.CompletedTask;
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                lock (m_privateCleanupLock)
                {
                    cleanup.Completion.TrySetException(failure);
                }
                return new AggregateException("Unpublished batch cleanup failed.", failure);
            }

            lock (m_privateCleanupLock)
            {
                cleanup.Stage = PrivateCleanupStage.WaitingForConformance;
            }
            bool deferred = !drain.IsCompleted ||
                cleanup.Candidates.Any(
                    static candidate => !candidate.Generation.ResourceOwnership.IsCleanupReady ||
                        !candidate.Generation.IsQuiescent);
            if (deferred && !ExecutionContext.IsFlowSuppressed())
            {
                ExecutionContext.SuppressFlow();
                try
                {
                    _ = CompletePrivateBatchCleanupAsync(cleanup, drain);
                }
                finally
                {
                    ExecutionContext.RestoreFlow();
                }
            }
            else
            {
                _ = CompletePrivateBatchCleanupAsync(cleanup, drain);
            }
            if (deferred)
            {
                return new NodeManagerCleanupDeferredException(completion);
            }
            try
            {
                await completion.ConfigureAwait(false);
                return null;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                return new AggregateException("Unpublished batch cleanup failed.", failure);
            }
        }

        /// <summary>
        /// Captures the physical completion of private batches still owned by this lifecycle.
        /// </summary>
        private Task<bool[]>? CapturePrivateCleanupCompletion()
        {
            lock (m_privateCleanupLock)
            {
                return m_privateCleanups.Count == 0
                    ? null
                    : Task.WhenAll(m_privateCleanups.Select(cleanup => cleanup.Completion.Task));
            }
        }

        /// <summary>
        /// Drains explicit reads before destroying any candidate or its staging dependencies.
        /// </summary>
        private async Task CompletePrivateBatchCleanupAsync(PrivateBatchCleanup cleanup, Task drain)
        {
            try
            {
                await drain.ConfigureAwait(false);
                while (true)
                {
                    PrivateCandidateCleanup candidate;
                    lock (m_privateCleanupLock)
                    {
                        if (cleanup.Candidates.Count == 0)
                        {
                            m_privateCleanups.Remove(cleanup);
                            break;
                        }
                        candidate = cleanup.Candidates[0];
                    }

                    lock (m_privateCleanupLock)
                    {
                        cleanup.Stage = PrivateCleanupStage.ProducerShutdown;
                    }
                    await CompletePrivateCandidateCleanupAsync(cleanup, candidate).ConfigureAwait(false);
                    lock (m_privateCleanupLock)
                    {
                        cleanup.Candidates.RemoveAt(0);
                    }
                }
                lock (m_privateCleanupLock)
                {
                    cleanup.Completion.TrySetResult(true);
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                lock (m_privateCleanupLock)
                {
                    cleanup.Completion.TrySetException(failure);
                }
            }
            catch (OutOfMemoryException failure)
            {
                cleanup.Completion.TrySetException(failure);
                throw;
            }
        }

        /// <summary>
        /// Cleans one private candidate while retaining its exact recovery admission and confirmation outcome.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// A verified cleanup confirmation must be reconciled before private cleanup can continue.
        /// </exception>
        /// <exception cref="AggregateException">
        /// Private abort and retaining its cleanup failure both fail,
        /// or private cleanup and restoring recovery admission both fail.
        /// </exception>
        private async ValueTask CompletePrivateCandidateCleanupAsync(
            PrivateBatchCleanup cleanup,
            PrivateCandidateCleanup candidate)
        {
            NodeManagerGeneration generation = candidate.Generation;
            GenerationGate recoveryGate = generation.CleanupRecoveryGate;
            try
            {
                if (generation.Gate.State == NodeManagerGenerationState.Prepared)
                {
                    generation.Gate.Abort();
                }
                using NodeManagerGenerationRegistry.ResourceLifetime.CleanupLease physicalCleanup =
                    await generation.ResourceOwnership.EnterCleanupAsync(generation).ConfigureAwait(false);
                recoveryGate.Retire();
                await recoveryGate.WaitForDrainAsync().ConfigureAwait(false);
                if (!generation.TryClaimCleanupConfirmation(out NodeManagerCleanupFailureHandle? confirmationClaim))
                {
                    throw new InvalidOperationException(
                        "A verified cleanup confirmation must be reconciled before private cleanup can continue.");
                }
                bool completed = false;
                try
                {
                    NodeManagerCleanupRecovery.EnsureNoRetainedFailures(generation);
                    await physicalCleanup.PrepareForShutdownAsync(generation).ConfigureAwait(false);
                    NodeManagerCleanupRecovery.EnsureNoRetainedFailures(generation);
                    await physicalCleanup.WaitForQuiescenceAsync().ConfigureAwait(false);
                    if (!candidate.AbortCompleted)
                    {
                        if (candidate.AbortFailure is { } abortFailure)
                        {
                            ExceptionDispatchInfo.Capture(abortFailure).Throw();
                        }
                        lock (m_privateCleanupLock)
                        {
                            cleanup.Stage = PrivateCleanupStage.Abort;
                        }
                        if (candidate.Prepared is { } prepared)
                        {
                            try
                            {
                                await cleanup.Host.AbortAsync(prepared, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception failure) when (failure is not OutOfMemoryException)
                            {
                                candidate.AbortFailure = failure;
                                try
                                {
                                    NodeManagerCleanupRecovery.RetainPrivateAbortFailure(
                                        candidate.Manager,
                                        generation,
                                        failure,
                                        () =>
                                        {
                                            lock (m_privateCleanupLock)
                                            {
                                                candidate.AbortCompleted = true;
                                                candidate.ManagerDisposed = true;
                                                cleanup.Stage = PrivateCleanupStage.CompletedStage;
                                            }
                                        });
                                }
                                catch (Exception retentionFailure) when (retentionFailure is not OutOfMemoryException)
                                {
                                    throw new AggregateException(
                                        "Private abort and cleanup failure retention failed.",
                                        failure,
                                        retentionFailure);
                                }
                                throw;
                            }
                        }
                        candidate.AbortCompleted = true;
                    }
                    if (generation.Gate.State == NodeManagerGenerationState.Prepared)
                    {
                        generation.Gate.Abort();
                    }
                    CheckPrivateCleanupFailures(cleanup, candidate);
                    await DisposeNodeManagerAsync(cleanup, candidate).ConfigureAwait(false);
                    recoveryGate.DisposeGeneration();
                    NodeManagerGenerationRegistry.CompleteCleanup(generation);
                    completed = true;
                }
                finally
                {
                    if (confirmationClaim is { } confirmation)
                    {
                        generation.FinishCleanupConfirmationClaim(confirmation, completed);
                    }
                }
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                lock (m_privateCleanupLock)
                {
                    if (recoveryGate.State == NodeManagerGenerationState.Retiring)
                    {
                        try
                        {
                            recoveryGate.Restore();
                        }
                        catch (Exception restoreFailure) when (restoreFailure is not OutOfMemoryException)
                        {
                            throw new AggregateException(
                                "Private cleanup and recovery admission failed.", failure, restoreFailure);
                        }
                    }
                }
                throw;
            }
            catch (OutOfMemoryException failure)
            {
                try
                {
                    if (recoveryGate.State == NodeManagerGenerationState.Retiring)
                    {
                        recoveryGate.Restore();
                    }
                }
                finally
                {
                    cleanup.Completion.TrySetException(failure);
                }
                throw;
            }
        }

        /// <summary>
        /// Resumes an exact confirmed private candidate only after a retryable or completed stage.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// An earlier candidate must finish first, or the failed cleanup stage cannot safely be retried automatically.
        /// </exception>
        internal async ValueTask ResumePrivateCleanupAfterConfirmationAsync(NodeManagerGeneration generation)
        {
            PrivateBatchCleanup? cleanup;
            Task completion;
            bool start = false;
            lock (m_privateCleanupLock)
            {
                cleanup = m_privateCleanups.FirstOrDefault(current =>
                    current.Candidates.Any(candidate => ReferenceEquals(candidate.Generation, generation)));
                if (cleanup is null ||
                    cleanup.Stage is
                        PrivateCleanupStage.WaitingForConformance or PrivateCleanupStage.WaitingForShutdown)
                {
                    return;
                }
                if (!ReferenceEquals(cleanup.Candidates[0].Generation, generation))
                {
                    throw new InvalidOperationException(
                        "A preceding private candidate must finish cleanup before this candidate can resume.");
                }
                if (!cleanup.Completion.Task.IsCompleted)
                {
                    completion = cleanup.Completion.Task;
                }
                else
                {
                    if (cleanup.Stage is not (
                        PrivateCleanupStage.ProducerShutdown or PrivateCleanupStage.CompletedStage))
                    {
                        throw new InvalidOperationException(
                            "The failed private cleanup stage cannot safely be retried automatically.");
                    }
                    cleanup.Completion = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    completion = cleanup.Completion.Task;
                    start = true;
                }
            }
            if (start)
            {
                if (!ExecutionContext.IsFlowSuppressed())
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        _ = CompletePrivateBatchCleanupAsync(cleanup, Task.CompletedTask);
                    }
                }
                else
                {
                    _ = CompletePrivateBatchCleanupAsync(cleanup, Task.CompletedTask);
                }
            }
            await completion.ConfigureAwait(false);
        }

        /// <summary>
        /// Identifies the current stage of private candidate cleanup.
        /// </summary>
        private enum PrivateCleanupStage
        {
            Reserving,
            WaitingForShutdown,
            WaitingForConformance,
            ProducerShutdown,
            Abort,
            Disposal,
            StagingDisposal,
            CompletedStage
        }

        /// <summary>
        /// Owns the unfinished candidates and physical cleanup result of one private batch.
        /// </summary>
        private sealed class PrivateBatchCleanup(
            IDynamicNodeManagerHost host,
            List<PrivateCandidateCleanup> candidates)
        {
            /// <summary>
            /// Gets the host used for candidate abort.
            /// </summary>
            internal IDynamicNodeManagerHost Host { get; } = host;

            /// <summary>
            /// Gets the candidates still owned by this cleanup.
            /// </summary>
            internal List<PrivateCandidateCleanup> Candidates { get; } = candidates;

            /// <summary>
            /// Gets the completion of the complete physical cleanup.
            /// </summary>
            internal TaskCompletionSource<bool> Completion { get; set; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets or sets the current cleanup stage.
            /// </summary>
            internal PrivateCleanupStage Stage { get; set; }
        }

        /// <summary>
        /// Retains one exact candidate, its prepared state, and its recovery identity.
        /// </summary>
        private sealed class PrivateCandidateCleanup(
            IAsyncNodeManager manager,
            PreparedNodeManager? prepared,
            NodeManagerGeneration generation)
        {
            /// <summary>Gets the candidate retained by the cleanup owner.</summary>
            internal IAsyncNodeManager Manager { get; } = manager;

            /// <summary>Gets its prepared state, if preparation started.</summary>
            internal PreparedNodeManager? Prepared { get; } = prepared;

            /// <summary>Gets the candidate's recovery identity.</summary>
            internal NodeManagerGeneration Generation { get; } = generation;

            /// <summary>Gets or sets whether the private abort callback completed.</summary>
            internal bool AbortCompleted { get; set; }

            /// <summary>
            /// Gets or sets the original abort failure, preventing unverified callback replay.
            /// </summary>
            internal Exception? AbortFailure { get; set; }

            /// <summary>Gets or sets whether candidate metadata was detached.</summary>
            internal bool Detached { get; set; }

            /// <summary>Gets or sets whether manager disposal completed.</summary>
            internal bool ManagerDisposed { get; set; }

            /// <summary>Gets or sets whether staged resource disposal completed.</summary>
            internal bool StagingDisposed { get; set; }
        }

        /// <summary>
        /// Tracks the state of one change of a batch while it is prepared and committed.
        /// </summary>
        private sealed class BatchEntry
        {
            /// <summary>
            /// Creates an entry.
            /// </summary>
            /// <param name="change">The requested change.</param>
            /// <param name="current">The registration state being replaced or retired, or null.</param>
            public BatchEntry(LifecycleChange change, RegistrationState? current)
            {
                Change = change;
                Current = current;
            }

            /// <summary>
            /// Gets the requested change.
            /// </summary>
            public LifecycleChange Change { get; }

            /// <summary>
            /// Gets the registration state being replaced or retired, or null for an addition.
            /// </summary>
            public RegistrationState? Current { get; }

            /// <summary>
            /// Gets or sets the node manager created for the change.
            /// </summary>
            public IAsyncNodeManager? Manager
            {
                get => m_manager;
                set => m_manager = value;
            }

            /// <summary>
            /// Gets or sets the prepared node manager of the change.
            /// </summary>
            public PreparedNodeManager? Prepared { get; set; }

            /// <summary>
            /// Gets or sets the registration state that replaces the current one.
            /// </summary>
            public RegistrationState? Next { get; set; }

            /// <summary>
            /// Gets or sets the external references dropped by the change.
            /// </summary>
            public List<LocalReference> DroppedReferences { get; set; } = [];

            /// <summary>
            /// Transfers the private manager to the sole disposal caller, including after a failed abort.
            /// </summary>
            /// <returns>
            /// The manager to dispose, or null after disposal ownership was transferred or published.
            /// </returns>
            public IAsyncNodeManager? TakePrivateManager()
            {
                return Prepared?.Published == true ? null : Interlocked.Exchange(ref m_manager, null);
            }

            /// <summary>The manager owned by this private entry until publication or abort disposal.</summary>
            private IAsyncNodeManager? m_manager;
        }

        /// <summary>
        /// Data owned by one preparation until publication or transfer to independent private cleanup.
        /// </summary>
        private sealed class PreparationPayload
        {
            /// <summary>
            /// Creates a prepared batch.
            /// </summary>
            /// <param name="owner">The lifecycle that prepared the batch.</param>
            /// <param name="server">The running server.</param>
            /// <param name="host">The dynamic node manager host.</param>
            /// <param name="entries">The prepared entries.</param>
            /// <param name="operation">The lifecycle operation held until the batch ends.</param>
            /// <param name="namespaceCount">The number of namespaces when the batch was prepared.</param>
            /// <param name="routingRevision">The serving view captured before candidate callbacks.</param>
            /// <param name="typeTree">The private type tree image.</param>
            /// <param name="originalTypes">The published type tree to restore.</param>
            /// <param name="typeRevision">The revision of the published type tree.</param>
            /// <param name="factory">The private encodeable factory image.</param>
            /// <param name="originalFactory">The published encodeable factory.</param>
            /// <param name="factoryRevision">The revision of the published factory.</param>
            /// <param name="resolver">The data type definition resolver, or null.</param>
            /// <param name="preparation">The explicit private staging metadata.</param>
            /// <param name="publication">The optimistic publication session, or null for a standalone batch.</param>
            /// <param name="initial">The explicit initial-publication capability, or null for runtime changes.</param>
            /// <param name="shutdown">The explicit shutdown publication capability, or null.</param>
            /// <param name="callbackPermit">The callback permit captured when preparation began, if any.</param>
            public PreparationPayload(
                NodeManagerLifecycle owner,
                IServerInternal server,
                IDynamicNodeManagerHost host,
                List<BatchEntry> entries,
                OperationLifetime operation,
                int namespaceCount,
                NodeManagerRoutingTable.RoutingSnapshot routingRevision,
                TypeTable typeTree,
                TypeTable originalTypes,
                long typeRevision,
                EncodeableFactory factory,
                EncodeableFactory originalFactory,
                long factoryRevision,
                IDataTypeDefinitionResolver? resolver,
                ChangePreparer preparation,
                LifecycleWriter.PublicationSession? publication,
                InitialPublication? initial,
                ShutdownPublication? shutdown,
                NodeManagerCallbackPermit? callbackPermit)
            {
                Owner = owner;
                Preparation = preparation;
                Server = server;
                Host = host;
                Entries = entries;
                Operation = operation;
                NamespaceCount = namespaceCount;
                RoutingRevision = routingRevision;
                TypeTree = typeTree;
                OriginalTypes = originalTypes;
                TypeRevision = typeRevision;
                Factory = factory;
                OriginalFactory = originalFactory;
                FactoryRevision = factoryRevision;
                Resolver = resolver;
                Publication = publication;
                Initial = initial;
                Shutdown = shutdown;
                CallbackPermit = callbackPermit;
                Registrations = entries.Where(entry => entry.Next is not null)
                    .Select(entry => new NodeManagerRegistrationSnapshot(entry.Next!.Registration)).ToArrayOf();
            }

            /// <summary>
            /// Gets the registration and cleanup owner while preparation is active.
            /// </summary>
            public NodeManagerLifecycle Owner { get; }

            /// <summary>
            /// Gets the operation transferred to this preparation.
            /// </summary>
            public OperationLifetime Operation { get; }

            /// <summary>
            /// Gets the explicit staging metadata owned by this preparation.
            /// </summary>
            public ChangePreparer Preparation { get; }

            /// <summary>
            /// Gets the optimistic publication session that owns this prepared batch, if any.
            /// </summary>
            public LifecycleWriter.PublicationSession? Publication { get; }

            /// <summary>
            /// Gets the bootstrap capability that keeps runtime readiness separate from view publication.
            /// </summary>
            public InitialPublication? Initial { get; }

            /// <summary>
            /// Gets removal-only writer admission retained by this shutdown invocation.
            /// </summary>
            public ShutdownPublication? Shutdown { get; }

            /// <summary>
            /// Gets the callback permit captured when this batch was prepared.
            /// </summary>
            public NodeManagerCallbackPermit? CallbackPermit { get; }

            /// <summary>
            /// Gets the registrations the batch publishes.
            /// </summary>
            public ArrayOf<NodeManagerRegistrationSnapshot> Registrations { get; }

            /// <summary>
            /// Gets or sets whether the batch was committed.
            /// </summary>
            public bool IsCommitted { get; set; }

            /// <summary>
            /// Gets or sets whether the durable decision succeeded before routing publication.
            /// </summary>
            public bool DecisionCommitted { get; set; }

            /// <summary>
            /// Gets the server the batch was prepared for.
            /// </summary>
            public IServerInternal Server { get; }

            /// <summary>
            /// Gets the host the batch was prepared for.
            /// </summary>
            public IDynamicNodeManagerHost Host { get; }

            /// <summary>
            /// Gets the entries of the batch.
            /// </summary>
            public List<BatchEntry> Entries { get; }

            /// <summary>
            /// Gets the number of namespaces when the batch was prepared.
            /// </summary>
            public int NamespaceCount { get; }

            /// <summary>
            /// Gets the serving view that must remain current until publication admission.
            /// </summary>
            public NodeManagerRoutingTable.RoutingSnapshot RoutingRevision { get; }

            /// <summary>
            /// Gets the private type tree image of the batch.
            /// </summary>
            public TypeTable TypeTree { get; }

            /// <summary>
            /// Gets the published type tree to restore.
            /// </summary>
            public TypeTable OriginalTypes { get; }

            /// <summary>
            /// Gets the revision of the published type tree.
            /// </summary>
            public long TypeRevision { get; }

            /// <summary>
            /// Gets the private encodeable factory image of the batch.
            /// </summary>
            public EncodeableFactory Factory { get; }

            /// <summary>
            /// Gets the published encodeable factory.
            /// </summary>
            public EncodeableFactory OriginalFactory { get; }

            /// <summary>
            /// Gets the revision of the published encodeable factory.
            /// </summary>
            public long FactoryRevision { get; }

            /// <summary>
            /// Gets the data type definition resolver, if any.
            /// </summary>
            public IDataTypeDefinitionResolver? Resolver { get; }

            /// <summary>
            /// Gets the read images published together with the batch.
            /// </summary>
            public ArrayOf<INodeManagerReadImage> ReadImages { get; set; }

            /// <summary>
            /// Gets the slot-checked metadata awaiting consumption.
            /// </summary>
            public Dictionary<NodeManagerSlot, INodeManagerReadImage> Metadata { get; } = [];
        }

        /// <summary>
        /// Native transaction state with a single preparation payload and cached terminal cleanup outcome.
        /// </summary>
        private sealed class PreparedChanges : IPreparedChanges
        {
            /// <summary>
            /// Takes ownership of private preparation data and captures non-owning inspection descriptors.
            /// </summary>
            public PreparedChanges(PreparationPayload payload, bool validationOnly)
            {
                m_payload = payload;
                m_validationOnly = validationOnly;
                Registrations = payload.Registrations;
                Slots = payload.Entries.Select(entry =>
                    entry.Next?.Registration.Slot ?? entry.Current!.Registration.Slot).ToArrayOf();
            }

            /// <inheritdoc/>
            public ArrayOf<NodeManagerSlot> Slots { get; private set; }

            /// <inheritdoc/>
            public ArrayOf<NodeManagerRegistrationSnapshot> Registrations { get; }

            /// <inheritdoc/>
            public bool IsCommitted
            {
                get
                {
                    lock (m_state)
                    {
                        return m_payload?.IsCommitted ?? m_committed;
                    }
                }
            }

            /// <inheritdoc/>
            public void SetMetadata(INodeManagerReadImage metadata)
            {
                SetMetadataCore(null, metadata);
            }

            /// <inheritdoc/>
            public void SetMetadata(NodeManagerSlot slot, INodeManagerReadImage metadata)
            {
                if (slot is null)
                {
                    throw new ArgumentNullException(nameof(slot));
                }
                SetMetadataCore(slot, metadata);
            }

            /// <inheritdoc/>
            public ValueTask<LifecycleResult> CommitAsync(
                Func<CancellationToken, ValueTask>? decideAsync = null,
                CancellationToken cancellationToken = default)
            {
                return CommitCoreAsync(decideAsync, null, cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<LifecycleResult> CommitAsync(
                Func<CancellationToken, ValueTask>? decideAsync,
                Action publishCommittedState,
                CancellationToken cancellationToken = default)
            {
                return CommitCoreAsync(
                    decideAsync,
                    publishCommittedState ?? throw new ArgumentNullException(nameof(publishCommittedState)),
                    cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                PreparationPayload payload;
                lock (m_state)
                {
                    m_payload?.Owner.ThrowIfDecisionCallbackActive();
                    if (m_consumed)
                    {
                        return new ValueTask(m_finished.Task);
                    }
                    payload = m_payload ??
                        throw new InvalidOperationException("The prepared changes have already been disposed.");
                    m_consumed = true;
                    m_inspectionEnded = true;
                    payload.Metadata.Clear();
                }
                _ = AbortAsync(payload);
                return new ValueTask(m_finished.Task);
            }

            /// <summary>
            /// Ends borrowed inspection access before validation or failure cleanup begins.
            /// </summary>
            internal void EndInspection()
            {
                lock (m_state)
                {
                    m_inspectionEnded = true;
                }
            }

            /// <summary>
            /// Validates without deciding, then joins the same single cleanup attempt as disposal.
            /// </summary>
            /// <exception cref="InvalidOperationException">This preparation is not a validation candidate.</exception>
            internal ValueTask ValidateAsync(CancellationToken cancellationToken)
            {
                PreparationPayload? payload;
                lock (m_state)
                {
                    if (!m_validationOnly)
                    {
                        throw new InvalidOperationException("Only validation candidates can be validated.");
                    }
                    payload = m_consumed ? null : m_payload;
                    if (payload is not null)
                    {
                        ConsumeMetadata(payload);
                    }
                }
                return new ValueTask(payload is null
                    ? RejectAfterDisposalAsync()
                    : CompleteAsync(payload, null, null, cancellationToken));
            }

            /// <summary>
            /// Resolves and validates one exact metadata owner before transaction consumption.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is null.</exception>
            /// <exception cref="ArgumentException">
            /// Metadata does not identify the exact requested generation,
            /// or its owner is neither a candidate nor an unchanged live generation in this unit.
            /// </exception>
            /// <exception cref="InvalidOperationException">
            /// Preparation has been consumed or inspection has ended, or metadata is already bound for the slot.
            /// </exception>
            private void SetMetadataCore(NodeManagerSlot? slot, INodeManagerReadImage metadata)
            {
                if (metadata is null)
                {
                    throw new ArgumentNullException(nameof(metadata));
                }
                lock (m_state)
                {
                    PreparationPayload payload = GetOpenPayload();
                    payload.Owner.ThrowIfDecisionCallbackActive();
                    if (metadata.Owner is not { } owner ||
                        !NodeManagerGenerationRegistry.TryGet(owner, out NodeManagerGeneration? generation) ||
                        generation is null ||
                        (slot is not null && !ReferenceEquals(generation.Slot, slot)))
                    {
                        throw new ArgumentException(
                            "Metadata must identify the exact generation owner.", nameof(metadata));
                    }
                    slot ??= generation.Slot;
                    RegistrationState? candidate = payload.Entries
                        .FirstOrDefault(entry => ReferenceEquals(entry.Next?.Registration.Slot, slot))?.Next;
                    bool validOwner;
                    if (candidate is not null)
                    {
                        validOwner = ReferenceEquals(candidate.Registration.Identity, generation);
                    }
                    else
                    {
                        lock (payload.Owner.m_registrationLock)
                        {
                            validOwner = !payload.Entries.Any(entry =>
                                    ReferenceEquals(entry.Current?.Registration.Slot, slot)) &&
                                payload.RoutingRevision.Generations.Any(
                                    current => ReferenceEquals(current, generation)) &&
                                payload.Owner.m_registrations.TryGetValue(slot.Id, out RegistrationState? live) &&
                                ReferenceEquals(live.Registration.Slot, slot) &&
                                ReferenceEquals(live.Registration.Identity, generation);
                        }
                    }
                    if (!validOwner)
                    {
                        throw new ArgumentException(
                            "Metadata must belong to a candidate or unchanged live generation in this unit.",
                            nameof(metadata));
                    }
                    if (!payload.Metadata.TryAdd(slot, metadata))
                    {
                        throw new InvalidOperationException("Metadata is already bound for this slot.");
                    }
                    if (payload.Entries.Count == 0)
                    {
                        Slots = [.. Slots, slot];
                    }
                }
            }

            /// <summary>
            /// Admits one commit, with overlapping callers joining its result instead of replaying decisions.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// This is a validation-only candidate, or its preparation is no longer open.
            /// </exception>
            private ValueTask<LifecycleResult> CommitCoreAsync(
                Func<CancellationToken, ValueTask>? decideAsync,
                Action? publishCommittedState,
                CancellationToken cancellationToken)
            {
                PreparationPayload payload;
                TaskCompletionSource<LifecycleResult> commitCompletion;
                lock (m_state)
                {
                    m_payload?.Owner.ThrowIfDecisionCallbackActive();
                    if (m_validationOnly)
                    {
                        throw new InvalidOperationException("A validation candidate cannot be committed.");
                    }
                    if (m_commitCompletion is { } completion)
                    {
                        return new ValueTask<LifecycleResult>(completion.Task);
                    }
                    if (m_consumed)
                    {
                        return new ValueTask<LifecycleResult>(RejectAfterDisposalAsync());
                    }
                    payload = GetOpenPayload();
                    ConsumeMetadata(payload);
                    commitCompletion = m_commitCompletion = new TaskCompletionSource<LifecycleResult>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                }
                _ = CompleteAsync(
                    payload, decideAsync ?? (static _ => default), publishCommittedState, cancellationToken);
                return new ValueTask<LifecycleResult>(commitCompletion.Task);
            }

            /// <summary>
            /// Executes one commit or validation and releases preparation after publication or cleanup transfer.
            /// </summary>
            // TODO: Remove this suppression when RCS1140 distinguishes failure on a separate completion task.
            [SuppressMessage(
                "Documentation",
                "RCS1140:Add exception to documentation comment",
                Justification = "The caught publication failure faults the commit completion, not this helper task.")]
            private async Task CompleteAsync(
                PreparationPayload payload,
                Func<CancellationToken, ValueTask>? decideAsync,
                Action? publishCommittedState,
                CancellationToken cancellationToken)
            {
                LifecycleResult? result = null;
                Exception? executionFailure = null;
                Exception? cleanupFailure = null;
                try
                {
                    try
                    {
                        result = await payload.Owner.CompleteChangesAsync(
                            payload, decideAsync, cancellationToken, publishCommittedState).ConfigureAwait(false);
                        if (!m_validationOnly && result is null)
                        {
                            throw new InvalidOperationException("A publication returned no committed result.");
                        }
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        executionFailure = failure;
                    }
                    if (!payload.IsCommitted &&
                        !payload.DecisionCommitted &&
                        (m_validationOnly || executionFailure is not null))
                    {
                        cleanupFailure = await CleanupAsync(payload, waitForCleanup: m_validationOnly)
                            .ConfigureAwait(false);
                    }
                }
                catch (OutOfMemoryException failure)
                {
                    m_commitCompletion?.TrySetException(failure);
                    m_finished.TrySetException(failure);
                    if (m_validationOnly)
                    {
                        throw;
                    }
                    return;
                }
                ReleasePayload(ref payload, cleanupFailure);
                if (executionFailure is not null && cleanupFailure is not null)
                {
                    executionFailure = new AggregateException(
                        m_validationOnly
                            ? "Private validation and candidate cleanup both failed."
                            : "The prepared commit failed and candidate cleanup also failed.",
                        executionFailure, cleanupFailure);
                }
                if (m_validationOnly)
                {
                    if ((executionFailure ?? cleanupFailure) is { } failure)
                    {
                        ExceptionDispatchInfo.Capture(failure).Throw();
                    }
                }
                else if (executionFailure is null)
                {
                    m_commitCompletion!.TrySetResult(result!);
                }
                else
                {
                    m_commitCompletion!.TrySetException(executionFailure);
                }
            }

            /// <summary>
            /// Joins terminal cleanup before rejecting consumed commit or validation without replaying preparation.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// The commit or validation preparation has already been consumed or disposed.
            /// </exception>
            private async Task<LifecycleResult> RejectAfterDisposalAsync()
            {
                await m_finished.Task.ConfigureAwait(false);
                throw new InvalidOperationException(m_validationOnly
                    ? "The validation preparation has been consumed."
                    : "The prepared changes have already been disposed.");
            }

            /// <summary>
            /// Transfers candidates to lifecycle cleanup exactly once and caches its first outcome.
            /// </summary>
            private async Task AbortAsync(PreparationPayload payload)
            {
                Exception? failure;
                try
                {
                    failure = await CleanupAsync(payload, waitForCleanup: m_validationOnly).ConfigureAwait(false);
                }
                catch (OutOfMemoryException fatal)
                {
                    m_finished.TrySetException(fatal);
                    return;
                }
                ReleasePayload(ref payload, failure);
            }

            /// <summary>
            /// Releases private resources through the existing lifecycle cleanup owner, retaining failed stages there.
            /// </summary>
            private static async ValueTask<Exception?> CleanupAsync(
                PreparationPayload payload, bool waitForCleanup = false)
            {
                try
                {
                    Exception? failure = await payload.Owner.AbortBatchEntriesAsync(
                        payload.Server, payload.Host, payload.Entries).ConfigureAwait(false);
                    if (waitForCleanup && failure is NodeManagerCleanupDeferredException deferred)
                    {
                        await deferred.Completion.ConfigureAwait(false);
                        return deferred.InnerException;
                    }
                    return failure;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    return failure;
                }
            }

            /// <summary>
            /// Freezes metadata once under the transaction state gate.
            /// </summary>
            private void ConsumeMetadata(PreparationPayload payload)
            {
                m_consumed = true;
                m_inspectionEnded = true;
                payload.ReadImages = payload.Metadata.Values.ToArrayOf();
                payload.Metadata.Clear();
            }

            /// <summary>
            /// Drops all preparation, provider and server references before exposing terminal completion.
            /// </summary>
            private void ReleasePayload(ref PreparationPayload payload, Exception? cleanupFailure)
            {
                lock (m_state)
                {
                    m_committed = payload.IsCommitted;
                    m_payload = null;
                    payload.ReadImages = default;
                }
                OperationLifetime operation = payload.Operation;
                LifecycleWriter.PublicationSession? publication = payload.Publication;
                payload = null!;
                operation.Dispose();
                publication?.ReleaseCompletedBatch(this, cleanupFailure);
                if (cleanupFailure is null)
                {
                    m_finished.TrySetResult(true);
                }
                else
                {
                    m_finished.TrySetException(cleanupFailure);
                }
            }

            /// <summary>
            /// Requires an unconsumed preparation whose inspection access has not ended.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// Preparation has been consumed, inspection has ended or the payload has been released.
            /// </exception>
            private PreparationPayload GetOpenPayload()
            {
                return !m_consumed && !m_inspectionEnded && m_payload is { } payload
                    ? payload
                    : throw new InvalidOperationException("The prepared changes have already been consumed.");
            }

            /// <summary>The private data retained only while this transaction owns preparation.</summary>
            private PreparationPayload? m_payload;

            /// <summary>The only transaction state gate.</summary>
            private readonly Lock m_state = new();

            /// <summary>The first disposal outcome shared with concurrent and later callers.</summary>
            private readonly TaskCompletionSource<bool> m_finished =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The result of the one admitted commit, with no preparation or callback ownership.</summary>
            private TaskCompletionSource<LifecycleResult>? m_commitCompletion;

            /// <summary>Whether publication crossed the routing commit point.</summary>
            private bool m_committed;

            /// <summary>Whether commit, validation or disposal has consumed preparation.</summary>
            private bool m_consumed;

            /// <summary>Whether inspection has relinquished metadata access.</summary>
            private bool m_inspectionEnded;

            /// <summary>Whether this unit admits validation only.</summary>
            private readonly bool m_validationOnly;
        }

        /// <inheritdoc/>
        public ILifecycleCapture CaptureChanges()
        {
            (IServerInternal server, IDynamicNodeManagerHost host) = GetRunningServer();
            return new LifecycleCapture(this, m_writer.CaptureRevision(server, host));
        }

        /// <summary>
        /// Begins independent publication ownership for captured serving revisions without excluding other callers.
        /// </summary>
        /// <param name="revision">The captured evidence, never recaptured by admission.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>The publication invocation.</returns>
        private PublicationWriter BeginWriter(
            LifecycleWriter.PublicationRevision revision,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LifecycleWriter.PublicationSession session = m_writer.BeginPublication(revision);
            return new PublicationWriter(this, session);
        }

        /// <summary>
        /// Immutable record of the routing, type and factory revisions captured for a publication.
        /// </summary>
        private sealed class LifecycleCapture : ILifecycleCapture
        {
            /// <summary>
            /// Captures non-owning provider and revision evidence without retaining a serving image.
            /// </summary>
            public LifecycleCapture(NodeManagerLifecycle owner, LifecycleWriter.PublicationRevision revision)
            {
                m_owner = new WeakReference<NodeManagerLifecycle>(owner);
                m_revision = revision;
            }

            /// <inheritdoc/>
            public ValueTask<ILifecycleWriter> BeginAsync(
                CancellationToken cancellationToken = default)
            {
                if (!m_owner.TryGetTarget(out NodeManagerLifecycle? owner))
                {
                    throw new ObjectDisposedException(nameof(INodeManagerLifecycle));
                }
                return new ValueTask<ILifecycleWriter>(owner.BeginWriter(m_revision, cancellationToken));
            }

            /// <summary>The non-owning lifecycle identity.</summary>
            private readonly WeakReference<NodeManagerLifecycle> m_owner;

            /// <summary>The non-owning captured serving evidence.</summary>
            private readonly LifecycleWriter.PublicationRevision m_revision;
        }

        /// <summary>
        /// Optimistic publication that prepares and owns batches until it is disposed.
        /// </summary>
        private sealed class PublicationWriter : ILifecycleWriter
        {
            /// <summary>
            /// Exposes the existing writer's publication session without a second admission owner.
            /// </summary>
            public PublicationWriter(NodeManagerLifecycle owner, LifecycleWriter.PublicationSession session)
            {
                m_owner = new WeakReference<NodeManagerLifecycle>(owner);
                Session = session;
            }

            /// <summary>
            /// Gets whether the invocation's expected serving revisions remain current.
            /// </summary>
            public bool IsCurrent => m_owner.TryGetTarget(out NodeManagerLifecycle? owner) &&
                Session.IsCurrentFor(owner.m_server);

            /// <summary>
            /// Gets the independently owned invocation and its expected serving revisions.
            /// </summary>
            private LifecycleWriter.PublicationSession Session { get; }

            /// <inheritdoc/>
            public ValueTask<IPreparedChanges> PrepareAsync(
                ArrayOf<LifecycleChange> changes, CancellationToken cancellationToken = default)
            {
                return PrepareCoreAsync(changes, default, cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<IPreparedChanges> PrepareReadImagesAsync(
                ArrayOf<INodeManagerReadImage> images, CancellationToken cancellationToken = default)
            {
                if (images.Count == 0)
                {
                    throw new ArgumentException("A read publication must contain at least one image.", nameof(images));
                }
                return PrepareCoreAsync([], images, cancellationToken);
            }

            /// <inheritdoc/>
            public async ValueTask ValidateAsync(
                ArrayOf<LifecycleChange> changes,
                Func<IPreparedChanges, CancellationToken, ValueTask> inspectAsync,
                CancellationToken cancellationToken = default)
            {
                if (inspectAsync is null)
                {
                    throw new ArgumentNullException(nameof(inspectAsync));
                }

                var candidate = (PreparedChanges)await PrepareCoreAsync(
                    changes, default, cancellationToken, validationOnly: true)
                    .ConfigureAwait(false);
                ValueTask inspection;
                try
                {
                    try
                    {
                        inspection = inspectAsync(candidate, cancellationToken);
                        await inspection.ConfigureAwait(false);
                    }
                    finally
                    {
                        inspectAsync = null!;
                        inspection = default;
                        candidate.EndInspection();
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    try
                    {
                        await candidate.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                    {
                        throw new AggregateException(
                            "Private validation and candidate cleanup both failed.",
                            failure,
                            cleanupFailure);
                    }
                    throw;
                }
                await candidate.ValidateAsync(cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Prepares a batch while ensuring that the invocation is current and prepares sequentially.
            /// </summary>
            /// <param name="changes">The changes of the unit.</param>
            /// <param name="images">The read images of the unit.</param>
            /// <param name="cancellationToken">The token used to cancel the operation.</param>
            /// <param name="validationOnly">Whether the batch is a validation candidate.</param>
            /// <returns>The prepared batch.</returns>
            private ValueTask<IPreparedChanges> PrepareCoreAsync(
                ArrayOf<LifecycleChange> changes,
                ArrayOf<INodeManagerReadImage> images,
                CancellationToken cancellationToken,
                bool validationOnly = false)
            {
                ArrayOf<LifecycleChange> capturedChanges = [.. changes];
                ArrayOf<INodeManagerReadImage> capturedImages = [.. images];
                return Session.PrepareAsync(() =>
                {
                    NodeManagerLifecycle owner = GetOwner();
                    NodeManagerCallbackPermit? callbackPermit = owner.CaptureLifecycleCallbackPermit();
                    return owner.PrepareChangesAsync(
                        capturedChanges, Session, callbackPermit, cancellationToken, capturedImages, validationOnly);
                });
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Session.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    if (m_owner.TryGetTarget(out NodeManagerLifecycle? owner))
                    {
                        owner.ScheduleRetiredGenerationDrainCleanup();
                    }
                }
            }

            /// <summary>
            /// Resolves the lifecycle only while active preparation needs its services.
            /// </summary>
            /// <exception cref="ObjectDisposedException">The captured lifecycle is no longer available.</exception>
            private NodeManagerLifecycle GetOwner()
            {
                return m_owner.TryGetTarget(out NodeManagerLifecycle? owner)
                    ? owner
                    : throw new ObjectDisposedException(nameof(INodeManagerLifecycle));
            }

            /// <summary>The non-owning lifecycle identity.</summary>
            private readonly WeakReference<NodeManagerLifecycle> m_owner;
        }

        /// <summary>The construction-bound server state and schema publication services.</summary>
        private readonly INodeManagerLifecycleHost m_server;

        /// <summary>The clock used for lifecycle wait diagnostics and retry delays.</summary>
        private readonly TimeProvider m_timeProvider;

        private readonly LifecycleWriter m_writer;
        private readonly Lock m_registrationLock = new();
        private Dictionary<Guid, RegistrationState> m_registrations = [];

        /// <summary>Protects retained private cleanup owners and their candidate lists.</summary>
        private readonly Lock m_privateCleanupLock = new();

        /// <summary>Owns private batches until every candidate and staging dependency is disposed.</summary>
        private readonly List<PrivateBatchCleanup> m_privateCleanups = [];

        /// <summary>Protects the lifecycle's immutable host recovery binding.</summary>
        private readonly Lock m_cleanupAuthorityLock = new();

        /// <summary>The host's bound recovery adapters, or a denied binding after unconfigured startup.</summary>
        private NodeManagerCleanupAuthority? m_cleanupAuthority;

        /// <summary>Whether server startup has closed recovery configuration.</summary>
        private bool m_cleanupAuthoritySealed;

        /// <summary>Owns retired generations independently of lifecycle publication.</summary>
        private readonly GenerationReclaimer m_reclaimer;
    }
}
