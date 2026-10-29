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
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Schema;
using Opc.Ua.Server.RuntimeNodeSet;
using RetiredGeneration = Opc.Ua.Server.GenerationReclaimer.RetiredGeneration;
using OperationLifetime = Opc.Ua.Server.LifecycleWriter.OperationLifetime;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Default live NodeManager lifecycle provider owned by a <see cref="StandardServer"/>.
    /// </summary>
    public sealed partial class NodeManagerLifecycle : INodeManagerPublicationLifecycle, IDisposable
    {
        /// <summary>
        /// Creates a lifecycle provider for a directly constructed server.
        /// </summary>
        /// <remarks>
        /// Additional handles forward publication to the server's single generation owner. Disposing a handle
        /// prevents new operations through it but does not dispose the server's generations or writer.
        /// </remarks>
        public NodeManagerLifecycle(StandardServer server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_canonicalOwner = server.NodeManagerLifecycle as NodeManagerLifecycle;
            m_writer = new LifecycleWriter(() => m_reclaimer.Count == 0 && m_registrations.Count == 0);
        }

        /// <inheritdoc/>
        public ArrayOf<NodeManagerRegistration> Registrations
        {
            get
            {
                if (m_canonicalOwner is { } owner)
                {
                    return owner.Registrations;
                }
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
                if (m_canonicalOwner is { } owner)
                {
                    return owner.Slots;
                }
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
        public bool IsShuttingDown => m_writer.IsShuttingDown || m_canonicalOwner?.IsShuttingDown == true;

        /// <inheritdoc/>
        public bool SupportsPreparedPublication =>
            m_server.CurrentInstance.NodeManager is IDynamicNodeManagerBatchHost;

        internal long ShutdownCleanupProgress =>
            m_canonicalOwner?.ShutdownCleanupProgress ?? m_reclaimer.CleanupProgress;

        internal int RetiredNodeManagerCount => m_canonicalOwner?.RetiredNodeManagerCount ?? m_reclaimer.Count;

        /// <summary>
        /// Gets whether any serving or retired generation still owns resources required from the server internals.
        /// </summary>
        internal bool HasPendingShutdownCleanup
        {
            get
            {
                if (m_canonicalOwner is { } owner)
                {
                    return owner.HasPendingShutdownCleanup;
                }
                lock (m_registrationLock)
                {
                    return m_registrations.Count != 0 || m_reclaimer.Count != 0;
                }
            }
        }

        internal void PrepareForStartup()
        {
            m_writer.BeginStartup(m_reclaimer.BeginStartup);
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
            if (m_canonicalOwner is { } owner)
            {
                await owner.BeginShutdownAsync(server, ct).ConfigureAwait(false);
                return;
            }

            Task activeOperations = await m_writer.BeginShutdownAsync(ct).ConfigureAwait(false);
            bool shutdownPrepared = false;
            try
            {
                await activeOperations.WaitAsync(ct).ConfigureAwait(false);
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
                if (failures.Count != 0)
                {
                    throw new AggregateException("Accepted NodeManager work failed to stop.", failures);
                }
                shutdownPrepared = true;
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
                await generation.PrepareForShutdownAsync().ConfigureAwait(false);
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
        internal async ValueTask CompleteShutdownAsync(
            IServerInternal server,
            bool retireSystems,
            CancellationToken ct = default)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (m_canonicalOwner is { } owner)
            {
                await owner.CompleteShutdownAsync(server, retireSystems, ct).ConfigureAwait(false);
                return;
            }

            await BeginShutdownAsync(server, ct).ConfigureAwait(false);
            Task activeOperations = await m_writer.BeginShutdownAsync(ct).ConfigureAwait(false);
            LifecycleWriter.ShutdownCleanup? cleanupOwnership = null;
            bool shutdownCompleted = false;
            try
            {
                await activeOperations.WaitAsync(ct).ConfigureAwait(false);
                cleanupOwnership = await m_writer.BeginShutdownCleanupAsync(ct).ConfigureAwait(false);
                m_reclaimer.StopScheduling();
                ArrayOf<NodeManagerBatchChange> applications;
                ArrayOf<NodeManagerBatchChange> systems;
                lock (m_registrationLock)
                {
                    applications = m_registrations.Values.Where(state => !state.IsSystem)
                        .Select(state => NodeManagerBatchChange.Remove(state.Registration)).ToArrayOf();
                    systems = retireSystems
                        ? m_registrations.Values.Where(state => state.IsSystem)
                            .Select(state => NodeManagerBatchChange.Remove(state.Registration)).ToArrayOf()
                        : [];
                }
                foreach (ArrayOf<NodeManagerBatchChange> changes in new[] { applications, systems })
                {
                    Exception? reconciliationFailure = null;
                    if (changes.Count != 0)
                    {
                        var shutdown = new ShutdownPublication(this, server, cleanupOwnership);
                        IPreparedNodeManagerBatch batch = await PrepareBatchAsync(
                            changes,
                            null,
                            CaptureLifecycleCallbackPermit(),
                            ct,
                            shutdown: shutdown).ConfigureAwait(false);
                        await using var lifetime = batch.ConfigureAwait(false);
                        NodeManagerBatchResult result = await batch.CommitAsync(
                            static _ => default, ct).ConfigureAwait(false);
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
                        reconciliationFailure is not null && cleanupFailure is not OutOfMemoryException)
                    {
                        throw new AggregateException(
                            "Shutdown reconciliation and retirement cleanup failed.",
                            reconciliationFailure,
                            cleanupFailure);
                    }
                    if (reconciliationFailure is not null)
                    {
                        throw reconciliationFailure;
                    }
                }
                shutdownCompleted = true;
            }
            finally
            {
                cleanupOwnership?.Dispose();
                m_writer.EndShutdown(!shutdownCompleted, completing: true);
            }
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
            if (state.Prepared.NodeManager is not INodeManagerReadinessNotification participant)
            {
                return;
            }

            ct.ThrowIfCancellationRequested();
            using var readiness = new NodeManagerReadinessScope(server, state.Registration.Identity);
            await participant.OnServerReadyAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            EnsurePublicationServer(server, host, initial);
            if (!IsCurrentRegistration(state.Registration))
            {
                throw new InvalidOperationException(
                    "The registration changed while its readiness operation was completing.");
            }
        }

        /// <summary>
        /// Clears the pending readiness flag of a registration.
        /// </summary>
        /// <param name="state">The registration state, or null.</param>
        private async Task ReleaseReadinessClaimAsync(RegistrationState? state)
        {
            if (state is not null)
            {
                lock (m_registrationLock)
                {
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
                await generation.FlushDeferredEventsAsync().ConfigureAwait(false);
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
                    state.Registration.Generation == registration.Generation &&
                    ReferenceEquals(
                        state.Registration.NodeManager,
                        registration.NodeManager);
            }
        }

        private bool IsOwnedNodeManager(IAsyncNodeManager nodeManager)
        {
            lock (m_registrationLock)
            {
                return m_registrations.Values.Any(state =>
                        AreSameNodeManager(
                            state.Registration.NodeManager,
                            nodeManager)) ||
                    m_reclaimer.Snapshot().Any(retired =>
                        AreSameNodeManager(
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
            bool allowPendingReadiness = false)
        {
            lock (m_registrationLock)
            {
                if (!m_registrations.TryGetValue(
                    registration.Id,
                    out RegistrationState? state) ||
                    state.Registration.Generation != registration.Generation ||
                    !ReferenceEquals(
                        state.Registration.NodeManager,
                        registration.NodeManager))
                {
                    throw new InvalidOperationException(
                        "The registration is stale or is not owned by this lifecycle provider.");
                }
                if (state.ReadinessPending && !allowPendingReadiness)
                {
                    throw new InvalidOperationException(
                        "The NodeManager is completing readiness. Retry after its startup operation completes.");
                }
                return state;
            }
        }

        private static async ValueTask<MonitoredItemTransition>
            PrepareMonitoredItemRemovalAsync(
                IAsyncNodeManager nodeManager,
                CancellationToken ct)
        {
            (INodeManagerMonitoredItemSource lifecycle, IReadOnlyList<IMonitoredItem> items) =
                await GetOwnedMonitoredItemsAsync(nodeManager, ct)
                    .ConfigureAwait(false);
            return new MonitoredItemTransition(
                lifecycle,
                replacement: null,
                compatibleItems: [],
                deletedItems: items,
                terminal: true);
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
                await PrepareMonitoredItemRemovalAsync(
                    nodeManager,
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

        private static async ValueTask<MonitoredItemTransition>
            PrepareMonitoredItemTransitionAsync(
                IAsyncNodeManager current,
                IAsyncNodeManager replacement,
                CancellationToken ct)
        {
            (INodeManagerMonitoredItemSource currentLifecycle,
                IReadOnlyList<IMonitoredItem> items) =
                await GetOwnedMonitoredItemsAsync(current, ct)
                    .ConfigureAwait(false);
            if (items.Count == 0)
            {
                return new MonitoredItemTransition(
                    currentLifecycle,
                    replacement: null,
                    compatibleItems: [],
                    deletedItems: []);
            }
            if (replacement is not INodeManagerMonitoredItemSource replacementLifecycle)
            {
                throw new NotSupportedException(
                    "The replacement NodeManager does not support monitored-item transitions.");
            }

            var compatibleItems = new List<IMonitoredItem>(items.Count);
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

        private static ValueTask<(
            INodeManagerMonitoredItemSource Lifecycle,
            IReadOnlyList<IMonitoredItem> Items)> GetOwnedMonitoredItemsAsync(
                IAsyncNodeManager nodeManager,
                CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(nodeManager);
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

            if (nodeManager is not INodeManagerMonitoredItemSource nodeManagerLifecycle)
            {
                if (items.Length > 0)
                {
                    throw new NotSupportedException(
                        "The NodeManager does not support monitored-item transitions.");
                }
                nodeManagerLifecycle = UnsupportedMonitoredItemLifecycle.Instance;
            }
            return new ValueTask<(INodeManagerMonitoredItemSource, IReadOnlyList<IMonitoredItem>)>(
                (nodeManagerLifecycle, items));
        }

        private static bool AreSameNodeManager(
            IAsyncNodeManager first,
            IAsyncNodeManager second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }
            INodeManager? firstSyncNodeManager = first.SyncNodeManager;
            INodeManager? secondSyncNodeManager = second.SyncNodeManager;
            return firstSyncNodeManager is not null &&
                secondSyncNodeManager is not null &&
                ReferenceEquals(
                    firstSyncNodeManager,
                    secondSyncNodeManager);
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

                if (serverObject.UrisVersion is not null)
                {
                    DateTimeUtc now = DateTimeUtc.Now;
                    uint version = serverObject.UrisVersion.Value;
                    serverObject.UrisVersion.Value =
                        Utils.IncrementIdentifier(ref version);
                    serverObject.UrisVersion.Timestamp = now;
                    serverObject.UrisVersion.UpdateChangeMasks(NodeStateChangeMasks.Value);
                    await reconciliation.RunAsync(
                        NodeManagerReconciliationStage.Namespaces,
                        generations,
                        () => serverObject.UrisVersion.ClearChangeMasksAsync(
                            server.DefaultSystemContext,
                            includeChildren: false,
                            CancellationToken.None)).ConfigureAwait(false);
                }
            }
        }

        private static async ValueTask DisposeNodeManagerAsync(
            IAsyncNodeManager nodeManager)
        {
            ChangePreparer.Detach(nodeManager);
            if (nodeManager is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (nodeManager is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private static async ValueTask<Exception?> TryDisposeNodeManagerAsync(
            IAsyncNodeManager nodeManager)
        {
            try
            {
                await DisposeNodeManagerAsync(nodeManager).ConfigureAwait(false);
                return null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return ex;
            }
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
            /// <param name="current">The lifecycle endpoint for the retiring NodeManager.</param>
            /// <param name="replacement">The lifecycle endpoint for the replacement NodeManager, if any.</param>
            /// <param name="compatibleItems">Items that can be handed to the replacement NodeManager.</param>
            /// <param name="deletedItems">Items whose nodes disappeared during reload.</param>
            /// <param name="isOwnedBySubscription">Optional test hook that verifies subscription ownership.</param>
            /// <param name="terminal">Whether lost items require client-side recreation.</param>
            public MonitoredItemTransition(
                INodeManagerMonitoredItemSource current,
                INodeManagerMonitoredItemSource? replacement,
                IReadOnlyList<IMonitoredItem> compatibleItems,
                IReadOnlyList<IMonitoredItem> deletedItems,
                Func<IMonitoredItem, bool>? isOwnedBySubscription = null,
                bool terminal = false)
            {
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
                    if (m_current.GetNotificationDrain(monitoredItem.NodeId) is { } drain)
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
                    long sourceRevision = binding?.SourceRevision ?? 0;
                    try
                    {
                        ServiceResult result = await m_current.ReleaseSourceAsync(
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
                        if (!compatible || m_replacement is null ||
                            binding?.IsRemoved == true || !IsOwnedBySubscription(monitoredItem))
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
                        m_markedDeletedItems.Contains(monitoredItem) || !IsOwnedBySubscription(monitoredItem) ||
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

            private readonly INodeManagerMonitoredItemSource m_current;
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

        private sealed class UnsupportedMonitoredItemLifecycle :
            INodeManagerMonitoredItemSource
        {
            public static UnsupportedMonitoredItemLifecycle Instance { get; } = new();

            public Func<CancellationToken, ValueTask>? GetNotificationDrain(NodeId nodeId)
            {
                return null;
            }

            public ValueTask<ServiceResult> ValidateSourceAsync(
                IMonitoredItem monitoredItem,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(
                    new ServiceResult(StatusCodes.BadNotSupported));
            }

            public ValueTask<ServiceResult> ReleaseSourceAsync(
                IMonitoredItem monitoredItem,
                long sourceRevision,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(
                    new ServiceResult(StatusCodes.BadNotSupported));
            }

            public ValueTask<ServiceResult> AdoptSourceAsync(
                IMonitoredItem monitoredItem,
                MonitoredItemAdoption adoption,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(
                    new ServiceResult(StatusCodes.BadNotSupported));
            }
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
            /// Gets or sets the shared producer-stop attempt for this registration.
            /// </summary>
            public Task? ShutdownPreparation { get; set; }

            /// <summary>
            /// Gets whether this registration supplies a protected system role.
            /// </summary>
            public bool IsSystem { get; init; }
        }

        /// <inheritdoc/>
        public ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default)
        {
            if (m_canonicalOwner is { } owner)
            {
                m_writer.EnsureRunning();
                return owner.PrepareAsync(changes, cancellationToken);
            }
            return PrepareBatchAsync(changes, null, CaptureLifecycleCallbackPermit(), cancellationToken);
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
        private async ValueTask<IPreparedNodeManagerBatch> PrepareBatchAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            PublicationInvocation? publication,
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
            foreach (NodeManagerBatchChange change in changes)
            {
                if (change is null)
                {
                    throw new ArgumentException("A publication change cannot be null.", nameof(changes));
                }
                if (change.Current is { } current)
                {
                    if (!identities.Add(current.Id))
                    {
                        throw new ArgumentException("A registration occurs twice in the same unit.", nameof(changes));
                    }
                    RegistrationState state = GetCurrentState(current, shutdown is not null);
                    if (state.IsSystem && shutdown is null)
                    {
                        throw new InvalidOperationException("System slots can only be withdrawn by server shutdown.");
                    }
                }
                if (change.Factory is { } factory)
                {
                    ValidateFactoryRegistration(factory, change.Current);
                }
            }
            if (shutdown is not null)
            {
                if (initial is not null || readImages.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Shutdown publication cannot include initialization or read images.");
                }
                shutdown.ValidateChanges(changes);
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
                    publication?.Session.ValidateRevision();
                    var preparation = new ChangePreparer(server);
                    NodeManagerRoutingTable.RoutingSnapshot routingRevision = host.RoutingRevision;
                    IDataTypeDefinitionResolver? resolver = null;
                    int namespaceCount = server.NamespaceUris.Count;
                    for (int index = 0; index < changes.Count; index++)
                    {
                        NodeManagerBatchChange change = changes[index];
                        RegistrationState? current = change.Current is null
                            ? null
                            : GetCurrentState(change.Current, shutdown is not null);
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
                        NodeManagerBatchChange change = entry.Change;
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
                            stagingServer.Attach(entry.Manager);
                            NodeManagerGeneration generation = current is null
                                ? NodeManagerGenerationRegistry.CreateAddition(entry.Manager)
                                : NodeManagerGenerationRegistry.CreateReplacement(
                                    current.Registration.Identity, entry.Manager);
                            entry.Prepared = await host.PrepareAsync(entry.Manager, cancellationToken)
                                .ConfigureAwait(false);
                            entry.Prepared.Preparation = preparation;
                            entry.Prepared.Generation = generation;
                            entry.Prepared.ReplacedNodeManager = current?.Prepared.NodeManager;
                            await ValidateDataTypeCompatibilityAsync(stagingServer, entry.Manager, cancellationToken)
                                .ConfigureAwait(false);
                            if (current is not null)
                            {
                                if (current.Prepared.NodeManager is not INodeManagerReloadParticipant participant)
                                {
                                    throw new NotSupportedException("A batch replacement does not support safe reload.");
                                }
                                entry.DroppedReferences =
                                [
                                    .. await participant.PrepareReloadAsync(entry.Manager, cancellationToken)
                                        .ConfigureAwait(false)
                                ];
                            }
                            resolver = await m_server.RefreshComplexTypesAsync(
                                stagingServer, entry.Manager, publishResolver: false, cancellationToken: cancellationToken)
                                .ConfigureAwait(false) ??
                                resolver;
                            var registration = new NodeManagerRegistration(
                                current?.Registration.Id ?? Guid.NewGuid(),
                                (current?.Registration.Generation ?? 0) + 1,
                                entry.Manager);
                            entry.Next = new RegistrationState(registration, entry.Prepared)
                            {
                                ReadinessPending = entry.Manager is INodeManagerReadinessNotification
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
                    var prepared = new PreparedBatch(
                        this, server, host, entries, operation, namespaceCount, routingRevision,
                        preparation.TypeTree, preparation.OriginalTypes,
                        preparation.TypeRevision, preparation.Factory, preparation.OriginalFactory,
                        preparation.FactoryRevision, resolver, validationOnly, preparation, publication?.Session,
                        initial, shutdown, callbackPermit);
                    if (readImages.Count != 0)
                    {
                        prepared.BindReadImages(readImages);
                    }
                    operation = null;
                    return prepared;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    mutation.Dispose();
                    ReleaseAdmissions();
                    Exception? cleanup = await AbortBatchEntriesAsync(host, entries)
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
        private async ValueTask<NodeManagerBatchResult?> CompleteBatchAsync(
            PreparedBatch batch,
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
            long publishedRevision = 0;
            NodeManagerSlot[] publishedSlots = [];
            GenerationReconciler reconciler = GetGenerationReconciler(batch.Server);
            GenerationReconciler.ReconciliationWork reconciliation =
                reconciler.CreateReconciliation(() => EnterLifecycleOperation(), () => m_writer.IsShuttingDown);
            try
            {
                EnsurePublicationServer(batch.Server, batch.Host, batch.Initial, batch.Shutdown);
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
                            GetCurrentState(entry.Current.Registration, batch.Shutdown is not null), entry.Current))
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
                            MonitoredItemTransition transition =
                                entry.Change.TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate &&
                                entry.Manager is { } replacement
                                    ? await PrepareMonitoredItemTransitionAsync(
                                        entry.Current.Prepared.NodeManager,
                                        replacement,
                                        cancellationToken).ConfigureAwait(false)
                                    : await PrepareMonitoredItemRemovalAsync(
                                        entry.Current.Prepared.NodeManager,
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
                    m_reclaimer.Reserve(retired.Count);
                }

                NodeManagerGeneration[] candidateGenerations =
                    (batch.Initial?.SystemRegistrations.ToArray() ?? [])
                    .Select(registration => registration.Identity)
                    .Concat((candidates.ToArray() ?? []).Select(candidate => candidate.Generation!)).ToArray();
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
                    reconciliation.Publish(publishedRevision);
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

                try
                {
                    var context = new NodeManagerBatchContext(
                        Candidates: candidates,
                        Removed: removed,
                        TypeTree: batch.TypeTree,
                        OriginalTypes: batch.OriginalTypes,
                        TypeRevision: batch.TypeRevision,
                        Factory: batch.Factory,
                        OriginalFactory: batch.OriginalFactory,
                        FactoryRevision: batch.FactoryRevision,
                        ReadImages: batch.ReadImages,
                        DecideAsync: decideAsync is null
                            ? null
                            : token =>
                            {
                                batch.Publication?.ValidateRevision();
                                return mutation.DecideAsync(decideAsync, token);
                            },
                        ViewPublished: () =>
                        {
                            batch.Publication?.AdvanceRevision();
                            lock (m_registrationLock)
                            {
                                if (batch.IsCommitted)
                                {
                                    return;
                                }
                                batch.IsCommitted = true;
                                m_reclaimer.Enqueue(retired);
                                m_registrations = committedRegistrations;
                                foreach (BatchEntry entry in batch.Entries)
                                {
                                    if (entry.Current is not null)
                                    {
                                        entry.Current.Prepared.Published = false;
                                        if (entry.Change.TransitionMode !=
                                            NodeManagerMonitoredItemTransitionMode.Migrate || entry.Manager is null)
                                        {
                                            NodeManagerGeneration generation = entry.Current.Registration.Identity;
                                            generation.Slot.LateMigrationRevision =
                                                unchecked(generation.Slot.LateMigrationRevision + 1);
                                        }
                                    }
                                }
                                foreach (RetiredGeneration retirement in retired)
                                {
                                    retirement.LateMigrationRevision = retirement.Generation.Slot.LateMigrationRevision;
                                }
                                publishedRevision =
                                    batch.Host.RoutingRevision.Revision;
                            }
                            if (batch.Resolver is not null)
                            {
                                m_server.ComplexTypeResolverHolder?.SetResolver(batch.Resolver);
                            }
                            reconciler.SupersedeReconciliations();
                        },
                        Published: PublishCommittedStateAsync,
                        ReconcileBindingsAsync: async () =>
                        {
                            if (batch.Initial is { } initial)
                            {
                                await initial.WaitForRuntimeAsync().ConfigureAwait(false);
                            }
                            // Retired generations are cut off by their gate; physical source bindings are released
                            // by retirement cleanup once admitted work has drained.
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
                                    reconciliation.ReportOwnedFailure(
                                        NodeManagerReconciliationStage.MonitoredItems, failure);
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
                        },
                        DeferReferenceRemoval: (owner, references) =>
                        {
                            RetiredGeneration? retirement = retired.FirstOrDefault(item =>
                                ReferenceEquals(item.NodeManager, owner));
                            if (retirement is null)
                            {
                                throw new InvalidOperationException(
                                    "A deferred reference removal has no retiring NodeManager.");
                            }
                            retirement.PendingReferences.AddRange(references);
                        },
                        ReportCleanupFailure: failure => reconciliation.ReportOwnedFailure(
                            NodeManagerReconciliationStage.Publication, failure),
                        CancellationToken: cancellationToken,
                        Preparation: batch.Preparation,
                        Reconciliation: reconciliation,
                        References: references,
                        CallbackPermit: batch.CallbackPermit,
                        Admission: admission);
                    await batch.Host.CommitBatchAsync(context).ConfigureAwait(false);
                }
                catch (Exception failure) when (batch.IsCommitted && failure is not OutOfMemoryException)
                {
                    reconciliation.ReportOwnedFailure(NodeManagerReconciliationStage.Publication, failure);
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
                foreach ((RegistrationState state, bool recoverSources) in readinessStates)
                {
                    NodeManagerGeneration generation = state.Registration.Identity;
                    bool readinessFailed = true;
                    try
                    {
                        if (recoverSources)
                        {
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
                        readinessFailed = false;
                    }
                    finally
                    {
                        // A retained-event delivery failure surfaces unless a readiness failure is already in flight.
                        try
                        {
                            await ReleaseReadinessClaimAsync(state).ConfigureAwait(false);
                        }
                        catch (AggregateException) when (readinessFailed)
                        {
                        }
                    }
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
                return new NodeManagerBatchResult(
                    batch.Registrations, completed,
                    failures.Count == 0 ? null : new AggregateException(
                        "The batch committed; generation reconciliation remains pending.", failures),
                    publishedRevision,
                    publishedSlots,
                    Task.WhenAll(retired.Select(item => item.Retirement)),
                    reconciliation.Ticket);
            }
            finally
            {
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
                    foreach (BatchEntry entry in batch.Entries)
                    {
                        try
                        {
                            await ReleaseReadinessClaimAsync(entry.Next).ConfigureAwait(false);
                        }
                        catch (AggregateException)
                        {
                            // Registrations that completed readiness already surfaced their delivery failures;
                            // this cleanup only runs for the rest while the operation failure is in flight.
                        }
                    }
                    m_reclaimer.Activate(retired);
                    ScheduleRetiredGenerationDrainCleanup();
                }
            }
        }

        /// <summary>
        /// Rejects lifecycle re-entry from the durable decision callback.
        /// </summary>
        private void ThrowIfDecisionCallbackActive()
        {
            m_writer.ThrowIfDecisionCallbackActive();
        }

        /// <summary>
        /// Aborts private candidates and disposes created managers in reverse preparation order.
        /// </summary>
        /// <param name="host">The dynamic node manager host.</param>
        /// <param name="entries">The batch entries to abort.</param>
        /// <returns>The aggregated cleanup failure, or null if the cleanup succeeded.</returns>
        private async ValueTask<Exception?> AbortBatchEntriesAsync(
            IDynamicNodeManagerHost host,
            List<BatchEntry> entries)
        {
            var failures = new List<Exception>();
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                BatchEntry entry = entries[index];
                if (entry.Prepared is not null)
                {
                    try
                    {
                        await host.AbortAsync(entry.Prepared, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        failures.Add(failure);
                    }
                }
                if (entry.Manager is not null && entry.Prepared?.Published != true)
                {
                    NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(entry.Manager);
                    if (generation.Gate.State == NodeManagerGenerationState.Prepared)
                    {
                        generation.Gate.Abort();
                    }
                    Exception? failure = await TryDisposeNodeManagerAsync(entry.Manager).ConfigureAwait(false);
                    if (failure is not null)
                    {
                        failures.Add(failure);
                    }
                }
            }
            return failures.Count == 0 ? null : new AggregateException("Unpublished batch cleanup failed.", failures);
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
            public BatchEntry(NodeManagerBatchChange change, RegistrationState? current)
            {
                Change = change;
                Current = current;
            }

            /// <summary>
            /// Gets the requested change.
            /// </summary>
            public NodeManagerBatchChange Change { get; }

            /// <summary>
            /// Gets the registration state being replaced or retired, or null for an addition.
            /// </summary>
            public RegistrationState? Current { get; }

            /// <summary>
            /// Gets or sets the node manager created for the change.
            /// </summary>
            public IAsyncNodeManager? Manager { get; set; }

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
        }

        /// <summary>
        /// Prepared publication unit that holds the lifecycle operation until it is committed or disposed.
        /// </summary>
        private sealed class PreparedBatch : IPreparedNodeManagerBatch
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
            /// <param name="validationOnly">Whether the batch is a validation candidate.</param>
            /// <param name="preparation">The explicit private staging metadata.</param>
            /// <param name="publication">The optimistic publication session, or null for a standalone batch.</param>
            /// <param name="initial">The explicit initial-publication capability, or null for runtime changes.</param>
            /// <param name="shutdown">The explicit shutdown publication capability, or null.</param>
            /// <param name="callbackPermit">The callback permit captured when preparation began, if any.</param>
            public PreparedBatch(
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
                bool validationOnly,
                ChangePreparer preparation,
                LifecycleWriter.PublicationSession? publication,
                InitialPublication? initial,
                ShutdownPublication? shutdown,
                NodeManagerCallbackPermit? callbackPermit)
            {
                m_owner = owner;
                Preparation = preparation;
                Server = server;
                Host = host;
                Entries = entries;
                m_operation = operation;
                NamespaceCount = namespaceCount;
                RoutingRevision = routingRevision;
                TypeTree = typeTree;
                OriginalTypes = originalTypes;
                TypeRevision = typeRevision;
                Factory = factory;
                OriginalFactory = originalFactory;
                FactoryRevision = factoryRevision;
                Resolver = resolver;
                IsValidationOnly = validationOnly;
                Publication = publication;
                Initial = initial;
                Shutdown = shutdown;
                CallbackPermit = callbackPermit;
                Registrations = entries.Where(entry => entry.Next is not null)
                    .Select(entry => entry.Next!.Registration).ToArrayOf();
            }

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
            public ArrayOf<NodeManagerRegistration> Registrations { get; }

            /// <summary>
            /// Gets or sets whether the batch was committed.
            /// </summary>
            public bool IsCommitted { get; set; }

            /// <summary>
            /// Gets whether the batch is a validation candidate that cannot be committed.
            /// </summary>
            public bool IsValidationOnly { get; }

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
            public ArrayOf<INodeManagerReadImage> ReadImages { get; private set; }

            /// <summary>
            /// Binds the read images that are published with the batch. This can be done once before the batch is
            /// consumed.
            /// </summary>
            /// <param name="images">The read images to bind.</param>
            public void BindReadImages(ArrayOf<INodeManagerReadImage> images)
            {
                if (images.IsNull)
                {
                    throw new ArgumentNullException(nameof(images));
                }
                ArrayOf<INodeManagerReadImage> captured = [.. images];
                lock (m_stateGate)
                {
                    if (m_state != 0 || m_readImagesBound)
                    {
                        throw new InvalidOperationException(
                            "Read images must be bound once before consuming the batch.");
                    }
                    ReadImages = captured;
                    m_readImagesBound = true;
                }
            }

            /// <summary>
            /// Commits the batch.
            /// </summary>
            /// <param name="decideAsync">The durable decision callback.</param>
            /// <param name="cancellationToken">The token used to cancel the operation.</param>
            /// <returns>The committed result.</returns>
            public ValueTask<NodeManagerBatchResult> CommitAsync(
                Func<CancellationToken, ValueTask> decideAsync,
                CancellationToken cancellationToken = default)
            {
                return CommitCoreAsync(decideAsync, null, cancellationToken);
            }

            /// <summary>
            /// Commits the batch and invokes a callback once the committed image is installed.
            /// </summary>
            /// <param name="decideAsync">The durable decision callback.</param>
            /// <param name="publishCommittedState">The callback invoked after the commit.</param>
            /// <param name="cancellationToken">The token used to cancel the operation.</param>
            /// <returns>The committed result.</returns>
            public ValueTask<NodeManagerBatchResult> CommitAsync(
                Func<CancellationToken, ValueTask> decideAsync,
                Action publishCommittedState,
                CancellationToken cancellationToken = default)
            {
                return CommitCoreAsync(
                    decideAsync,
                    publishCommittedState ?? throw new ArgumentNullException(nameof(publishCommittedState)),
                    cancellationToken);
            }

            /// <summary>
            /// Validates the batch without committing it.
            /// </summary>
            /// <param name="cancellationToken">The token used to cancel the operation.</param>
            /// <returns>A task that completes when the validation finished.</returns>
            public async ValueTask ValidateAsync(CancellationToken cancellationToken)
            {
                _ = await CompleteAsync(null, null, cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Rolls back the batch if it was not committed and releases its lifecycle operation.
            /// </summary>
            /// <returns>A task that completes when the cleanup finished.</returns>
            public async ValueTask DisposeAsync()
            {
                m_owner.ThrowIfDecisionCallbackActive();

                int state;
                lock (m_stateGate)
                {
                    state = Interlocked.CompareExchange(ref m_state, 2, 0);
                }
                if (state == 1)
                {
                    await m_finished.Task.ConfigureAwait(false);
                }
                OperationLifetime? operation = Interlocked.Exchange(ref m_operation, null);
                try
                {
                    if (operation is null)
                    {
                        return;
                    }
                    using IDisposable ambientOperation = operation.Activate();
                    if (!IsCommitted)
                    {
                        Exception? failure = await m_owner.AbortBatchEntriesAsync(
                            Host, Entries).ConfigureAwait(false);
                        if (failure is not null)
                        {
                            throw failure;
                        }
                    }
                }
                finally
                {
                    operation?.Dispose();
                }
            }

            private async ValueTask<NodeManagerBatchResult> CommitCoreAsync(
                Func<CancellationToken, ValueTask> decideAsync,
                Action? publishCommittedState,
                CancellationToken cancellationToken)
            {
                if (decideAsync is null)
                {
                    throw new ArgumentNullException(nameof(decideAsync));
                }
                return await CompleteAsync(decideAsync, publishCommittedState, cancellationToken)
                    .ConfigureAwait(false) ??
                    throw new InvalidOperationException("A publication returned no committed result.");
            }

            private async ValueTask<NodeManagerBatchResult?> CompleteAsync(
                Func<CancellationToken, ValueTask>? decideAsync,
                Action? publishCommittedState,
                CancellationToken cancellationToken)
            {
                m_owner.ThrowIfDecisionCallbackActive();

                if (IsValidationOnly != (decideAsync is null))
                {
                    throw new InvalidOperationException("A validation candidate cannot be committed.");
                }
                lock (m_stateGate)
                {
                    if (Interlocked.CompareExchange(ref m_state, 1, 0) != 0)
                    {
                        throw new InvalidOperationException("The prepared batch has already been consumed.");
                    }
                }
                try
                {
                    using IDisposable ambientOperation = (m_operation ??
                        throw new InvalidOperationException("The prepared batch no longer owns its lifecycle operation."))
                        .Activate();
                    return await m_owner.CompleteBatchAsync(
                        this, decideAsync, cancellationToken, publishCommittedState).ConfigureAwait(false);
                }
                catch (NodeManagerRoutingConflictException failure) when (
                    failure.Batch is null && ReferenceEquals(failure.RoutingRevision, RoutingRevision))
                {
                    throw new InvalidOperationException(
                        failure.Message, new NodeManagerRoutingConflictException(failure, this));
                }
                finally
                {
                    if (IsCommitted)
                    {
                        Interlocked.Exchange(ref m_operation, null)?.Dispose();
                    }
                    Volatile.Write(ref m_state, 2);
                    m_finished.TrySetResult(true);
                }
            }

            private readonly NodeManagerLifecycle m_owner;
            private readonly Lock m_stateGate = new();
            private readonly TaskCompletionSource<bool> m_finished =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private OperationLifetime? m_operation;
            private int m_state;
            private bool m_readImagesBound;
        }

        /// <inheritdoc/>
        public bool SupportsPublicationIsolation =>
            SupportsPreparedPublication && m_server.CurrentInstance.Factory is EncodeableFactory;

        /// <inheritdoc/>
        public INodeManagerPublicationCapture CapturePublication()
        {
            if (m_canonicalOwner is { } owner)
            {
                m_writer.EnsureRunning();
                return owner.CapturePublication();
            }
            (IServerInternal server, IDynamicNodeManagerHost host) = GetRunningServer();
            return new PublicationCapture(this, m_writer.CaptureRevision(server, host));
        }

        /// <summary>
        /// Begins independent publication ownership for captured serving revisions without excluding other callers.
        /// </summary>
        /// <param name="capture">The capture the invocation belongs to.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>The publication invocation.</returns>
        private PublicationInvocation BeginPublication(
            PublicationCapture capture,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSameRunningServer(capture.Revision.Server, capture.Revision.OwnerHost);
            LifecycleWriter.PublicationSession session = m_writer.BeginPublication(capture.Revision);
            return new PublicationInvocation(this, session);
        }

        /// <summary>
        /// Immutable record of the routing, type and factory revisions captured for a publication.
        /// </summary>
        /// <param name="Owner">The lifecycle that created the capture.</param>
        /// <param name="Revision">The writer-owned serving revision capture.</param>
        private sealed record PublicationCapture(
            NodeManagerLifecycle Owner,
            LifecycleWriter.PublicationRevision Revision) : INodeManagerPublicationCapture
        {
            /// <inheritdoc/>
            public ValueTask<INodeManagerPublication> BeginAsync(
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<INodeManagerPublication>(
                    Owner.BeginPublication(this, cancellationToken));
            }
        }

        /// <summary>
        /// Optimistic publication that prepares and owns batches until it is disposed.
        /// </summary>
        /// <param name="owner">The lifecycle that admitted the publication.</param>
        /// <param name="session">The independently owned invocation and its expected serving revisions.</param>
        private sealed class PublicationInvocation(
            NodeManagerLifecycle owner,
            LifecycleWriter.PublicationSession session) : INodeManagerValidationPublication
        {
            /// <summary>
            /// Gets whether the invocation's expected serving revisions remain current.
            /// </summary>
            public bool IsCurrent => Session.IsCurrent;

            /// <summary>
            /// Gets the independently owned invocation and its expected serving revisions.
            /// </summary>
            public LifecycleWriter.PublicationSession Session { get; } = session;

            /// <inheritdoc/>
            public ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
                ArrayOf<NodeManagerBatchChange> changes, CancellationToken cancellationToken = default)
            {
                return PrepareCoreAsync(changes, default, cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask<IPreparedNodeManagerBatch> PrepareReadImagesAsync(
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
                ArrayOf<NodeManagerBatchChange> changes,
                Func<IPreparedNodeManagerBatch, CancellationToken, ValueTask> inspectAsync,
                CancellationToken cancellationToken = default)
            {
                if (inspectAsync is null)
                {
                    throw new ArgumentNullException(nameof(inspectAsync));
                }

                IPreparedNodeManagerBatch batch = await PrepareCoreAsync(
                    changes, default, cancellationToken, validationOnly: true)
                    .ConfigureAwait(false);
                await using var lifetime = batch.ConfigureAwait(false);
                if (batch is not PreparedBatch candidate)
                {
                    throw new InvalidOperationException("Validation requires the captured lifecycle batch owner.");
                }
                await inspectAsync(batch, cancellationToken).ConfigureAwait(false);
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
            private ValueTask<IPreparedNodeManagerBatch> PrepareCoreAsync(
                ArrayOf<NodeManagerBatchChange> changes,
                ArrayOf<INodeManagerReadImage> images,
                CancellationToken cancellationToken,
                bool validationOnly = false)
            {
                NodeManagerCallbackPermit? callbackPermit = owner.CaptureLifecycleCallbackPermit();
                return Session.PrepareAsync(() => owner.PrepareBatchAsync(
                    changes, this, callbackPermit, cancellationToken, images, validationOnly));
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
                    owner.ScheduleRetiredGenerationDrainCleanup();
                }
            }

        }


        private readonly StandardServer m_server;

        /// <summary>The server-owned coordinator, or null when this instance is that coordinator.</summary>
        private readonly NodeManagerLifecycle? m_canonicalOwner;

        private readonly LifecycleWriter m_writer;
        private readonly Lock m_registrationLock = new();
        private Dictionary<Guid, RegistrationState> m_registrations = [];

        /// <summary>Owns retired generations independently of lifecycle publication.</summary>
        private readonly GenerationReclaimer m_reclaimer = new();

    }
}
