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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Schema;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Factory opt-in for lifecycle operations initiated from OPC UA request callbacks.
    /// </summary>
    internal interface IRequestCallbackSafeNodeManagerFactory
    {
        /// <summary>
        /// Gets whether request callbacks may enter lifecycle work without deadlocking request drains.
        /// </summary>
        bool AllowLifecycleFromRequestCallback { get; }
    }

    /// <summary>
    /// NodeManager opt-in for lifecycle operations initiated from OPC UA request callbacks.
    /// </summary>
    internal interface IRequestCallbackSafeNodeManager
    {
        /// <summary>
        /// Gets whether request callbacks may enter lifecycle work without deadlocking request drains.
        /// </summary>
        bool AllowLifecycleFromRequestCallback { get; }
    }

    /// <summary>
    /// Default live NodeManager lifecycle provider owned by a <see cref="StandardServer"/>.
    /// </summary>
    public sealed class NodeManagerLifecycle : INodeManagerPublicationLifecycle, IDisposable
    {
        /// <summary>
        /// Creates a lifecycle provider for a directly constructed server.
        /// </summary>
        public NodeManagerLifecycle(StandardServer server)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
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
                            .Select(state => state.Registration)
                            .ToArray());
                }
            }
        }

                /// <inheritdoc/>
        public bool IsShuttingDown => m_shuttingDown;

        internal long ShutdownCleanupProgress =>
            Interlocked.Read(ref m_shutdownCleanupProgress);

        internal int RetiredNodeManagerCount
        {
            get
            {
                lock (m_registrationLock)
                {
                    return m_retiredNodeManagers.Count;
                }
            }
        }

        internal void PrepareForStartup()
        {
            lock (m_operationLifetimeLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
                }
                if (!m_shuttingDown)
                {
                    return;
                }
                if (m_shutdownPrepared ||
                    m_activeLifecycleOperations != 0 ||
                    m_activeShutdownMethods != 0)
                {
                    throw new InvalidOperationException(
                        "The previous NodeManager lifecycle shutdown did not complete.");
                }
                m_shuttingDown = false;
            }
        }

        internal async ValueTask AdoptStartupNodeManagersAsync(
            IServerInternal server,
            CancellationToken ct = default)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (server.NodeManager is not IDynamicNodeManagerHost)
            {
                return;
            }

            using OperationLifetime operation = EnterLifecycleOperation();
            (IServerInternal currentServer, IDynamicNodeManagerHost host) =
                GetRunningServer();
            if (!ReferenceEquals(server, currentServer))
            {
                throw new InvalidOperationException(
                    "The running server changed before startup NodeManagers were adopted.");
            }

            await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureSameRunningServer(server, host, allowRequestCallback: false);
                ArrayOf<PreparedNodeManager> preparedNodeManagers = await host
                    .TakeStartupNodeManagersAsync(ct)
                    .ConfigureAwait(false);
                if (preparedNodeManagers.Count == 0)
                {
                    return;
                }

                var pending = new (
                    NodeManagerRegistration Registration,
                    PreparedNodeManager Prepared)[preparedNodeManagers.Count];
                lock (m_registrationLock)
                {
                    for (int ii = 0; ii < preparedNodeManagers.Count; ii++)
                    {
                        PreparedNodeManager prepared = preparedNodeManagers[ii];
                        Guid registrationId;
                        do
                        {
                            registrationId = Guid.NewGuid();
                        }
                        while (m_registrations.ContainsKey(registrationId));

                        pending[ii] = (
                            new NodeManagerRegistration(
                                registrationId,
                                1,
                                prepared.NodeManager),
                            prepared);
                    }

                    for (int ii = 0; ii < pending.Length; ii++)
                    {
                        (NodeManagerRegistration registration, PreparedNodeManager prepared) =
                            pending[ii];
                        m_registrations.Add(
                            registration.Id,
                            new RegistrationState(
                                registration,
                                prepared,
                                prepared.AllowLifecycleFromRequestCallback));
                    }
                }
            }
            finally
            {
                m_lifecycleSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Signal only: Dispose is synchronous. A drain already running
            // finishes retiring the generations it captured.
            m_backgroundWork.Dispose();

            bool disposeSemaphore;
            lock (m_operationLifetimeLock)
            {
                if (m_disposeRequested)
                {
                    return;
                }

                m_disposeRequested = true;
                m_disposed = true;
                m_shuttingDown = true;
                disposeSemaphore = TryReserveSemaphoreDisposal();
            }
            if (disposeSemaphore)
            {
                m_lifecycleSemaphore.Dispose();
            }
        }

        /// <summary>
        /// Prepares a lifecycle that was previously shut down to accept operations again after a server restart.
        /// </summary>
        internal void BeginStartup()
        {
            lock (m_operationLifetimeLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
                }
                if (!m_shuttingDown)
                {
                    return;
                }
                if (m_activeLifecycleOperations != 0 ||
                    m_activeShutdownMethods != 0 ||
                    m_shutdownPrepared)
                {
                    throw new InvalidOperationException(
                        "The previous NodeManager lifecycle has not completed shutdown.");
                }
                lock (m_registrationLock)
                {
                    if (m_registrations.Count != 0 || m_retiredNodeManagers.Count != 0)
                    {
                        throw new InvalidOperationException(
                            "The previous NodeManager generations have not completed cleanup.");
                    }
                    m_shuttingDown = false;
                }
            }
        }

        /// <summary>
        /// Notifies the initial node managers that the server is ready.
        /// </summary>
        /// <param name="initialManagers">The node managers created at startup.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <returns>A task that completes when all readiness participants have run.</returns>
        internal async ValueTask CompleteStartupAsync(
            ArrayOf<IAsyncNodeManager> initialManagers,
            CancellationToken ct = default)
        {
            using OperationLifetime operation = EnterLifecycleOperation();
            for (int ii = 0; ii < initialManagers.Count; ii++)
            {
                if (initialManagers[ii] is INodeManagerReadinessNotification participant)
                {
                    ct.ThrowIfCancellationRequested();
                    await participant.OnServerReadyAsync(ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                }
            }
        }

        internal async ValueTask BeginShutdownAsync(
            IServerInternal server,
            CancellationToken ct = default)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            Task activeOperations = await EnterShutdownAfterPublicationAsync(ct).ConfigureAwait(false);
            bool semaphoreHeld = false;
            bool shutdownPrepared = false;
            try
            {
                await activeOperations.WaitAsync(ct).ConfigureAwait(false);
                await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
                semaphoreHeld = true;
                if (server.NodeManager is IDynamicNodeManagerHost host)
                {
                    await CleanupRetiredNodeManagersAsync(
                        server,
                        host,
                        allowShuttingDown: true).ConfigureAwait(false);

                    RetiredNodeManager[] retired;
                    lock (m_registrationLock)
                    {
                        retired = [.. m_retiredNodeManagers];
                    }

                    foreach (RetiredNodeManager retiredNodeManager in retired)
                    {
                        host.SetRetiredGenerationNotifications(
                            retiredNodeManager.NodeManager,
                            enabled: false);
                        retiredNodeManager.NotificationsSuspended = true;
                    }
                }
                shutdownPrepared = true;
            }
            finally
            {
                if (semaphoreHeld)
                {
                    m_lifecycleSemaphore.Release();
                }
                ExitBeginShutdownMethod(shutdownPrepared);
            }
        }

        internal async ValueTask CompleteShutdownAsync(
            IServerInternal server,
            CancellationToken ct = default)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            Task activeOperations = await EnterShutdownAfterPublicationAsync(ct).ConfigureAwait(false);
            bool semaphoreHeld = false;
            bool shutdownCompleted = false;
            try
            {
                await activeOperations.WaitAsync(ct).ConfigureAwait(false);
                await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
                semaphoreHeld = true;
                RegistrationState[] registrations;
                RetiredNodeManager[] retired;
                lock (m_registrationLock)
                {
                    registrations = [.. m_registrations.Values];
                    retired = [.. m_retiredNodeManagers];
                }

                var host =
                    server.NodeManager as IDynamicNodeManagerHost;
                var failures = new List<Exception>();
                OperationCanceledException? cancellationException = null;
                foreach (RegistrationState registration in registrations)
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        ShutdownCleanupState cleanup = registration.ShutdownCleanup;
                        bool completeCommittedRemoval = cleanup.RemovalUnpublished;
                        if (completeCommittedRemoval && !cleanup.Detached)
                        {
                            cleanup.RemovalMonitoredItemsDeleted = true;
                            cleanup.Detached = true;
                            registration.Prepared.Staged = false;
                            RecordShutdownCleanupProgress();
                        }
                        await CleanupShutdownNodeManagerAsync(
                                server,
                                host,
                                registration.Prepared.NodeManager,
                                cleanup,
                                pendingReferences: null,
                                destroyAddressSpace: completeCommittedRemoval,
                                removeDestroyedExternalReferences: completeCommittedRemoval,
                                ct)
                            .ConfigureAwait(false);
                        lock (m_registrationLock)
                        {
                            if (m_registrations.TryGetValue(
                                    registration.Registration.Id,
                                    out RegistrationState? current) &&
                                ReferenceEquals(current, registration))
                            {
                                m_registrations.Remove(registration.Registration.Id);
                            }
                        }
                    }
                    catch (OperationCanceledException ex) when (
                        ct.IsCancellationRequested)
                    {
                        cancellationException = ex;
                        break;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        failures.Add(
                            new InvalidOperationException(
                                "A registered NodeManager failed during shutdown cleanup.",
                                ex));
                    }
                }

                if (cancellationException is null)
                {
                    foreach (RetiredNodeManager retiredNodeManager in retired)
                    {
                        try
                        {
                            ct.ThrowIfCancellationRequested();
                            await CleanupShutdownNodeManagerAsync(
                                    server,
                                    host,
                                    retiredNodeManager.NodeManager,
                                    retiredNodeManager.ShutdownCleanup,
                                    retiredNodeManager.PendingReferences,
                                    destroyAddressSpace: true,
                                    removeDestroyedExternalReferences: false,
                                    ct)
                                .ConfigureAwait(false);
                            RemoveRetiredNodeManagerRecord(retiredNodeManager);
                        }
                        catch (OperationCanceledException ex) when (
                            ct.IsCancellationRequested)
                        {
                            cancellationException = ex;
                            break;
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            failures.Add(
                                new InvalidOperationException(
                                    "A retired NodeManager failed during shutdown cleanup.",
                                    ex));
                        }
                    }
                }

                if (cancellationException is not null)
                {
                    throw cancellationException;
                }
                if (failures.Count > 0)
                {
                    throw new AggregateException(
                        "One or more NodeManagers failed during shutdown cleanup.",
                        failures);
                }

                shutdownCompleted = true;
            }
            finally
            {
                if (semaphoreHeld)
                {
                    m_lifecycleSemaphore.Release();
                }
                ExitCompleteShutdownMethod(shutdownCompleted);
            }
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> AddAsync(
            IAsyncNodeManagerFactory factory,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            ValidateFactoryRegistration(factory, null);
            return AddCoreAsync(
                factory.CreateAsync,
                IsRequestCallbackSafe(factory),
                callerContext,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> AddAsync(
            INodeManagerFactory factory,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return AddCoreAsync(
                (server, configuration, _) => new ValueTask<IAsyncNodeManager>(
                    factory.Create(server, configuration).ToAsyncNodeManager()),
                allowRequestCallback: false,
                callerContext,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            ValidateFactoryRegistration(replacement, registration);
            return ReloadCoreAsync(
                registration,
                replacement.CreateAsync,
                ReloadRetirementMode.Migrate,
                allowRequestCallback: IsRequestCallbackSafe(replacement),
                callerContext,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            return ReloadCoreAsync(
                registration,
                (server, configuration, _) => new ValueTask<IAsyncNodeManager>(
                    replacement.Create(server, configuration).ToAsyncNodeManager()),
                ReloadRetirementMode.Migrate,
                allowRequestCallback: false,
                callerContext,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            ValidateFactoryRegistration(replacement, registration);
            return ReloadCoreAsync(
                registration,
                replacement.CreateAsync,
                ReloadRetirementMode.Graceful,
                allowRequestCallback: IsRequestCallbackSafe(replacement),
                callerContext: null,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> ShadowReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            return ReloadCoreAsync(
                registration,
                (server, configuration, _) => new ValueTask<IAsyncNodeManager>(
                    replacement.Create(server, configuration).ToAsyncNodeManager()),
                ReloadRetirementMode.Graceful,
                allowRequestCallback: false,
                callerContext: null,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> ImmediateReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            ValidateFactoryRegistration(replacement, registration);
            return ReloadCoreAsync(
                registration,
                replacement.CreateAsync,
                ReloadRetirementMode.Immediate,
                allowRequestCallback: IsRequestCallbackSafe(replacement),
                callerContext: null,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> ImmediateReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            if (replacement is null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            return ReloadCoreAsync(
                registration,
                (server, configuration, _) => new ValueTask<IAsyncNodeManager>(
                    replacement.Create(server, configuration).ToAsyncNodeManager()),
                ReloadRetirementMode.Immediate,
                allowRequestCallback: false,
                callerContext: null,
                ct);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveAsync(
            NodeManagerRegistration registration,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            using OperationLifetime operation = EnterLifecycleOperation();
            RegistrationState permissionState = GetCurrentState(
                registration,
                allowRemovalRetry: true);
            bool allowRequestCallback =
                permissionState.AllowLifecycleFromRequestCallback;
            (IServerInternal entryServer, IDynamicNodeManagerHost entryHost) =
                GetRunningServer(allowRequestCallback, callerContext);
            using IDisposable? routing = (entryHost as IDynamicNodeManagerBatchHost)?.UseLiveRouting();
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? requestWaiter =
                EnterRequestLifecycleWaiter(entryServer, callerContext);
            await using var bindingAdmission =
                ((entryServer.NodeManager as IDynamicNodeManagerBatchHost)?.SuspendBindingAdmission(callerContext) ??
                    BindingAdmissionSuspension.Empty).ConfigureAwait(false);
            await WaitForLifecycleSemaphoreAsync(requestWaiter, ct)
                .ConfigureAwait(false);
            try
            {
                EnsureRequestCallbackAllowed(allowRequestCallback, callerContext);
                (IServerInternal cleanupServer, IDynamicNodeManagerHost cleanupHost) =
                    GetRunningServer(allowRequestCallback);
                await CleanupRetiredNodeManagersAsync(cleanupServer, cleanupHost)
                    .ConfigureAwait(false);

                RegistrationState state = GetCurrentState(
                    registration,
                    allowRemovalRetry: true);
                (IServerInternal server, IDynamicNodeManagerHost host) =
                    GetRunningServer(allowRequestCallback);
                ShutdownCleanupState cleanup = state.ShutdownCleanup;
                try
                {
                    if (!cleanup.Detached)
                    {
                        MonitoredItemTransition monitoredItemTransition =
                            await PrepareMonitoredItemRemovalAsync(
                                server,
                                state.Prepared.NodeManager,
                                ct).ConfigureAwait(false);
                        bool rollbackPublication = false;
                        if (!cleanup.RemovalUnpublished)
                        {
                            if (state.Prepared.Published)
                            {
                                await host
                                    .UnpublishAsync(
                                        state.Prepared.NodeManager,
                                        beforeUnpublish: () =>
                                            monitoredItemTransition.DetachCurrentAsync(ct),
                                        rollbackUnpublish: () =>
                                            monitoredItemTransition.RollbackAsync(
                                                CancellationToken.None),
                                        ct: ct)
                                    .ConfigureAwait(false);
                                state.Prepared.Published = false;
                                rollbackPublication = true;
                            }
                            else
                            {
                                await monitoredItemTransition
                                    .DetachCurrentAsync(ct)
                                    .ConfigureAwait(false);
                            }
                            MarkRemovalUnpublished(cleanup);
                        }
                        ClaimRemoval(registration, state);

                        try
                        {
                            await WaitForNotificationDispatchesOutsideLifecycleSemaphoreAsync(
                                    server,
                                    host,
                                    state.Prepared.NodeManager)
                                .ConfigureAwait(false);
                            ValidateRemovalClaim(
                                registration,
                                state,
                                "The registration was replaced while notifications drained.");
                            InvalidateContinuationPoints(
                                server,
                                state.Prepared.NodeManager);
                            await DrainRequestsOutsideLifecycleSemaphoreAsync(
                                    server,
                                    ct)
                                .ConfigureAwait(false);
                            EnsureSameRunningServer(
                                server,
                                host,
                                allowRequestCallback);
                            ValidateRemovalClaim(
                                registration,
                                state,
                                "The registration was replaced while removal requests drained.");
                            InvalidateContinuationPoints(
                                server,
                                state.Prepared.NodeManager);

                            // A request routed before the unpublish can still have created a
                            // monitored item on this manager while the drain waited for it.
                            if (HasActiveMonitoredItems(server, state.Prepared.NodeManager))
                            {
                                await DetachActiveMonitoredItemsAsync(
                                        server,
                                        state.Prepared.NodeManager)
                                    .ConfigureAwait(false);
                            }
                            await UnbindFromServerAsync(
                                server,
                                state.Prepared.NodeManager,
                                ct).ConfigureAwait(false);
                            state.Prepared.Staged = false;
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            if (!rollbackPublication ||
                                !CanRecoverRunningServer(server, host))
                            {
                                throw;
                            }

                            ServerBindings? rollbackBindings = null;
                            try
                            {
                                await host
                                    .PublishAsync(
                                        state.Prepared,
                                        CancellationToken.None)
                                    .ConfigureAwait(false);
                                rollbackBindings = await BindToServerAsync(
                                    server,
                                    state.Prepared.NodeManager,
                                    callerContext,
                                    CancellationToken.None).ConfigureAwait(false);
                                await CommitWithReconciliationAsync(
                                    server,
                                    host,
                                    state.Prepared,
                                    state.Prepared.NodeManager,
                                    rollbackBindings,
                                    callerContext,
                                    CancellationToken.None,
                                    afterCommit: () =>
                                        monitoredItemTransition.RollbackAsync(
                                            CancellationToken.None)).ConfigureAwait(false);
                                ResetRemovalUnpublished(cleanup);
                                lock (m_registrationLock)
                                {
                                    state.RemovalPending = false;
                                }
                                host.SetRetiredGenerationNotifications(
                                    state.Prepared.NodeManager,
                                    enabled: true);
                                await DrainRequestsOutsideLifecycleSemaphoreAsync(
                                        server,
                                        CancellationToken.None)
                                    .ConfigureAwait(false);
                                EnsureSameRunningServer(
                                    server,
                                    host,
                                    allowRequestCallback);
                                if (IsCurrentRegistration(registration))
                                {
                                    await ReconcileBindingsAsync(
                                        server,
                                        state.Prepared.NodeManager,
                                        rollbackBindings,
                                        callerContext,
                                        CancellationToken.None).ConfigureAwait(false);
                                }
                            }
                            catch (Exception rollbackException) when (
                                rollbackException is not OutOfMemoryException)
                            {
                                state.RemovalPending = false;
                                Exception? cleanupException = null;
                                if (state.Prepared.Published)
                                {
                                    ResetRemovalUnpublished(cleanup);
                                    host.SetRetiredGenerationNotifications(
                                        state.Prepared.NodeManager,
                                        enabled: true);
                                }
                                else
                                {
                                    try
                                    {
                                        if (rollbackBindings is not null)
                                        {
                                            await UnbindBindingsAsync(
                                                state.Prepared.NodeManager,
                                                rollbackBindings,
                                                CancellationToken.None).ConfigureAwait(false);
                                        }
                                        await UnbindFromServerAsync(
                                            server,
                                            state.Prepared.NodeManager,
                                            CancellationToken.None).ConfigureAwait(false);
                                        state.Prepared.Staged = false;
                                    }
                                    catch (Exception ex2) when (
                                        ex2 is not OutOfMemoryException)
                                    {
                                        cleanupException = ex2;
                                    }
                                }
                                if (cleanupException is not null)
                                {
                                    rollbackException = new AggregateException(
                                        "NodeManager rollback binding cleanup failed.",
                                        rollbackException,
                                        cleanupException);
                                }
                                throw new AggregateException(
                                    "NodeManager removal and rollback both failed.",
                                    ex,
                                    rollbackException);
                            }
                            throw;
                        }

                        MarkRemovalDetached(cleanup, monitoredItemTransition);
                    }
                    else
                    {
                        ClaimRemoval(registration, state);
                    }

                    if (!cleanup.NotificationsFinalized)
                    {
                        await FinalizeNotificationsOutsideLifecycleSemaphoreAsync(
                                server,
                                host,
                                state.Prepared.NodeManager)
                            .ConfigureAwait(false);
                        cleanup.NotificationsFinalized = true;
                        ValidateRemovalClaim(
                            registration,
                            state,
                            "The registration changed while notifications drained.");
                    }
                    if (!cleanup.Destroyed)
                    {
                        await DestroyAddressSpaceOutsideLifecycleSemaphoreAsync(
                                host,
                                state.Prepared.NodeManager,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        ValidateRemovalClaim(
                            registration,
                            state,
                            "The registration changed while its address space was destroyed.");
                        cleanup.NotificationsFinalized = true;
                        cleanup.Destroyed = true;
                    }
                    if (!cleanup.DestroyedExternalReferencesRemoved)
                    {
                        await host
                            .RemoveDestroyedExternalReferencesAsync(
                                state.Prepared.NodeManager,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        cleanup.DestroyedExternalReferencesRemoved = true;
                    }
                    if (!cleanup.Released)
                    {
                        host.Release(state.Prepared.NodeManager);
                        cleanup.Released = true;
                    }
                    if (!cleanup.Disposed)
                    {
                        RebuildActiveTypeTree(server);
                        await DisposeNodeManagerAsync(state.Prepared.NodeManager)
                            .ConfigureAwait(false);
                        cleanup.Disposed = true;
                    }
                    ValidateRemovalClaim(
                        registration,
                        state,
                        "The registration changed while removal cleanup completed.");
                    lock (m_registrationLock)
                    {
                        m_registrations.Remove(registration.Id);
                    }
                }
                catch
                {
                    state.RemovalPending = false;
                    throw;
                }

                if (!m_shuttingDown && !m_disposed)
                {
                    await NotifyCommittedChangeAsync(
                        server,
                        "removed",
                        namespaceCountBefore: server.NamespaceUris.Count,
                        ct: CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                m_lifecycleSemaphore.Release();
            }
        }

        private async ValueTask<NodeManagerRegistration> AddCoreAsync(
            CreateNodeManagerAsync createNodeManager,
            bool allowRequestCallback,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            using OperationLifetime operation = EnterLifecycleOperation();
            (IServerInternal entryServer, IDynamicNodeManagerHost entryHost) =
                GetRunningServer(allowRequestCallback, callerContext);
            using IDisposable? routing = (entryHost as IDynamicNodeManagerBatchHost)?.UseLiveRouting();
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? requestWaiter =
                EnterRequestLifecycleWaiter(entryServer, callerContext);
            await using var bindingAdmission =
                ((entryServer.NodeManager as IDynamicNodeManagerBatchHost)?.SuspendBindingAdmission(callerContext) ??
                    BindingAdmissionSuspension.Empty).ConfigureAwait(false);
            await WaitForLifecycleSemaphoreAsync(requestWaiter, ct)
                .ConfigureAwait(false);
            IAsyncNodeManager? nodeManager = null;
            PreparedNodeManager? prepared = null;
            IServerInternal? server = null;
            IDynamicNodeManagerHost? host = null;
            int namespaceCountBefore = 0;
            bool committed = false;
            RegistrationState? readinessState = null;
            try
            {
                (IServerInternal cleanupServer, IDynamicNodeManagerHost cleanupHost) =
                    GetRunningServer(allowRequestCallback);
                await CleanupRetiredNodeManagersAsync(cleanupServer, cleanupHost)
                    .ConfigureAwait(false);
                (server, host) = GetRunningServer(allowRequestCallback);
                namespaceCountBefore = server.NamespaceUris.Count;
                nodeManager = await createNodeManager(
                    server,
                    m_server.CurrentConfiguration,
                    ct).ConfigureAwait(false) ??
                    throw new InvalidOperationException(
                        "The NodeManager factory returned null.");
                if (IsOwnedNodeManager(nodeManager))
                {
                    throw new NodeManagerAlreadyRegisteredException();
                }
                prepared = await host.PrepareAsync(nodeManager, ct).ConfigureAwait(false);

                await ValidateDataTypeCompatibilityAsync(server, nodeManager, ct)
                    .ConfigureAwait(false);
                await m_server
                    .RefreshComplexTypesAsync(server, nodeManager, cancellationToken: ct)
                    .ConfigureAwait(false);
                ServerBindings bindings = await BindToServerAsync(
                    server,
                    nodeManager,
                    callerContext,
                    ct).ConfigureAwait(false);
                await host.PublishAsync(prepared, ct).ConfigureAwait(false);

                await CommitWithReconciliationAsync(
                    server,
                    host,
                    prepared,
                    nodeManager,
                    bindings,
                    callerContext,
                    ct,
                    afterCommit: () => RecoverDetachedMonitoredItemsAsync(
                        server,
                        nodeManager,
                        ct)).ConfigureAwait(false);
                RebuildTypeTree(nodeManager);

                var registration = new NodeManagerRegistration(
                    Guid.NewGuid(),
                    1,
                    nodeManager);
                readinessState = new RegistrationState(
                    registration,
                    prepared,
                    allowRequestCallback)
                {
                    ReadinessPending = nodeManager is INodeManagerReadinessNotification
                };
                lock (m_registrationLock)
                {
                    m_registrations.Add(registration.Id, readinessState);
                }
                committed = true;

                await DrainRequestsOutsideLifecycleSemaphoreAsync(
                        server,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                EnsureSameRunningServer(
                    server,
                    host,
                    allowRequestCallback);
                if (IsCurrentRegistration(registration))
                {
                    await ReconcileBindingsAsync(
                        server,
                        nodeManager,
                        bindings,
                        callerContext,
                        CancellationToken.None).ConfigureAwait(false);
                }
                await CompleteReadinessOutsideLifecycleSemaphoreAsync(
                    server, host, readinessState, ct).ConfigureAwait(false);
                if (!m_shuttingDown && !m_disposed)
                {
                    await NotifyCommittedChangeAsync(
                        server,
                        "added",
                        namespaceCountBefore,
                        CancellationToken.None).ConfigureAwait(false);
                }
                return registration;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (committed)
                {
                    throw new InvalidOperationException(
                        "The NodeManager was added, but post-commit binding, readiness or notification failed. " +
                        "The live registration remains available from Registrations.",
                        ex);
                }

                Exception? cleanupException = null;
                if (prepared is not null && host is not null)
                {
                    cleanupException = await CleanupPreparedAsync(
                        server,
                        host,
                        prepared,
                        allowRequestCallback).ConfigureAwait(false);
                }

                NodeManagerRegistration? retainedRegistration = null;
                Exception? recoveryException = null;
                if (prepared?.Published == true &&
                    nodeManager is not null &&
                    server is not null &&
                    host is not null)
                {
                    retainedRegistration = new NodeManagerRegistration(
                        Guid.NewGuid(),
                        1,
                        nodeManager);
                    lock (m_registrationLock)
                    {
                        m_registrations[retainedRegistration.Id] =
                            new RegistrationState(
                                retainedRegistration,
                                prepared,
                                allowRequestCallback);
                    }

                    if (CanRecoverRunningServer(server, host))
                    {
                        try
                        {
                            ServerBindings recoveryBindings =
                                await BindToServerAsync(
                                    server,
                                    nodeManager,
                                    callerContext,
                                    CancellationToken.None).ConfigureAwait(false);
                            await ReconcileBindingsAsync(
                                server,
                                nodeManager,
                                recoveryBindings,
                                callerContext,
                                CancellationToken.None).ConfigureAwait(false);
                            await DrainRequestsOutsideLifecycleSemaphoreAsync(
                                    server,
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                            EnsureSameRunningServer(
                                server,
                                host,
                                allowRequestCallback);
                            if (IsCurrentRegistration(retainedRegistration))
                            {
                                await ReconcileBindingsAsync(
                                    server,
                                    nodeManager,
                                    recoveryBindings,
                                    callerContext,
                                    CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                        catch (Exception recoveryFailure) when (
                            recoveryFailure is not OutOfMemoryException)
                        {
                            recoveryException = recoveryFailure;
                        }
                    }
                }

                if (server is not null && !m_disposed && !m_shuttingDown)
                {
                    if (nodeManager is not null)
                    {
                        RebuildActiveTypeTree(server);
                    }
                    await NotifyNamespaceTableChangedAsync(
                        server,
                        namespaceCountBefore,
                        CancellationToken.None).ConfigureAwait(false);
                }

                Exception? disposeException = null;
                if (nodeManager is not null &&
                    prepared?.Published != true &&
                    ex is not NodeManagerAlreadyRegisteredException)
                {
                    disposeException = await TryDisposeNodeManagerAsync(nodeManager)
                        .ConfigureAwait(false);
                }
                if (retainedRegistration is not null)
                {
                    var failures = new List<Exception> { ex };
                    if (cleanupException is not null)
                    {
                        failures.Add(cleanupException);
                    }
                    if (recoveryException is not null)
                    {
                        failures.Add(recoveryException);
                    }
                    throw new InvalidOperationException(
                        "NodeManager creation failed during rollback. " +
                        "The published generation was retained and is available " +
                        "from Registrations for retry or removal.",
                        new AggregateException(failures));
                }
                if (cleanupException is not null ||
                    disposeException is not null)
                {
                    var failures = new List<Exception> { ex };
                    if (cleanupException is not null)
                    {
                        failures.Add(cleanupException);
                    }
                    if (disposeException is not null)
                    {
                        failures.Add(disposeException);
                    }
                    throw new AggregateException(
                        "NodeManager creation and cleanup failed.",
                        failures);
                }
                throw;
            }
            finally
            {
                ReleaseReadinessClaim(readinessState);
                m_lifecycleSemaphore.Release();
            }
        }

        private async ValueTask<NodeManagerRegistration> ReloadCoreAsync(
            NodeManagerRegistration registration,
            CreateNodeManagerAsync createNodeManager,
            ReloadRetirementMode retirementMode,
            bool allowRequestCallback,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            using OperationLifetime operation = EnterLifecycleOperation();
            bool factoryAllowsRequestCallback = allowRequestCallback;
            RegistrationState permissionState = GetCurrentState(registration);
            allowRequestCallback = factoryAllowsRequestCallback &&
                permissionState.AllowLifecycleFromRequestCallback;
            (IServerInternal entryServer, IDynamicNodeManagerHost entryHost) =
                GetRunningServer(allowRequestCallback, callerContext);
            using IDisposable? routing = (entryHost as IDynamicNodeManagerBatchHost)?.UseLiveRouting();
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? requestWaiter =
                EnterRequestLifecycleWaiter(entryServer, callerContext);
            await using var bindingAdmission =
                ((entryServer.NodeManager as IDynamicNodeManagerBatchHost)?.SuspendBindingAdmission(callerContext) ??
                    BindingAdmissionSuspension.Empty).ConfigureAwait(false);
            await WaitForLifecycleSemaphoreAsync(requestWaiter, ct)
                .ConfigureAwait(false);
            IAsyncNodeManager? replacementManager = null;
            PreparedNodeManager? replacement = null;
            RegistrationState? current = null;
            RegistrationState? readinessState = null;
            List<LocalReference> droppedInboundReferences = [];
            IServerInternal? server = null;
            IDynamicNodeManagerHost? host = null;
            int namespaceCountBefore = 0;
            bool allowActiveMonitoredItems =
                retirementMode != ReloadRetirementMode.Migrate;
            bool deferForActiveMonitoredItems =
                retirementMode == ReloadRetirementMode.Graceful;
            try
            {
                EnsureRequestCallbackAllowed(allowRequestCallback, callerContext);
                (IServerInternal cleanupServer, IDynamicNodeManagerHost cleanupHost) =
                    GetRunningServer(allowRequestCallback);
                await CleanupRetiredNodeManagersAsync(cleanupServer, cleanupHost)
                    .ConfigureAwait(false);

                current = GetCurrentState(registration);
                allowRequestCallback = factoryAllowsRequestCallback &&
                    current.AllowLifecycleFromRequestCallback;
                EnsureRequestCallbackAllowed(allowRequestCallback, callerContext);
                (server, host) = GetRunningServer(allowRequestCallback);
                namespaceCountBefore = server.NamespaceUris.Count;
                replacementManager = await createNodeManager(
                    server,
                    m_server.CurrentConfiguration,
                    ct).ConfigureAwait(false) ??
                    throw new InvalidOperationException(
                        "The replacement NodeManager factory returned null.");
                if (IsOwnedNodeManager(replacementManager))
                {
                    throw new NodeManagerAlreadyRegisteredException();
                }
                replacement = await host
                    .PrepareAsync(replacementManager, ct)
                    .ConfigureAwait(false);
                await ValidateDataTypeCompatibilityAsync(
                    server,
                    replacementManager,
                    ct).ConfigureAwait(false);
                Func<ValueTask>? beforeTransitionCommit = null;
                Func<ValueTask>? afterTransitionCommit = null;
                Func<ValueTask>? rollbackTransitionCommit = null;
                if (!allowActiveMonitoredItems)
                {
                    MonitoredItemTransition monitoredItemTransition =
                        await PrepareMonitoredItemTransitionAsync(
                            server,
                            current.Prepared.NodeManager,
                            replacementManager,
                            ct).ConfigureAwait(false);
                    beforeTransitionCommit =
                        () => monitoredItemTransition.DetachCurrentAsync(ct);
                    afterTransitionCommit = async () =>
                    {
                        List<Exception> failures =
                            await monitoredItemTransition.AttachCompatibleAsync(
                                CancellationToken.None).ConfigureAwait(false);
                        monitoredItemTransition.MarkDeletedItems();
                        try
                        {
                            await RecoverDetachedMonitoredItemsAsync(
                                server,
                                replacementManager,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException)
                        {
                            failures.Add(ex);
                        }

                        if (failures.Count > 0)
                        {
                            throw new AggregateException(
                                "The replacement NodeManager was committed, but one or more " +
                                "monitored items could not be attached.",
                                failures);
                        }
                    };
                    rollbackTransitionCommit =
                        () => monitoredItemTransition.RollbackAsync(CancellationToken.None);
                }
                else if (retirementMode == ReloadRetirementMode.Immediate)
                {
                    MonitoredItemTransition monitoredItemTransition =
                        await PrepareMonitoredItemRemovalAsync(
                            server,
                            current.Prepared.NodeManager,
                            ct).ConfigureAwait(false);
                    beforeTransitionCommit =
                        () => monitoredItemTransition.DetachCurrentAsync(ct);
                    afterTransitionCommit = () =>
                    {
                        monitoredItemTransition.MarkDeletedItems();
                        return default;
                    };
                    rollbackTransitionCommit =
                        () => monitoredItemTransition.RollbackAsync(CancellationToken.None);
                }
                Func<ValueTask>? beforeReloadCommit = beforeTransitionCommit;
                ArrayOf<SemanticChangeStructureDataType> semanticChanges =
                    GetSemanticChanges(
                        current.Prepared.NodeManager,
                        replacementManager);
                if (current.Prepared.NodeManager is not
                    INodeManagerReloadParticipant reloadParticipant)
                {
                    throw new NotSupportedException(
                        "The current NodeManager does not support safe live reload.");
                }
                ArrayOf<LocalReference> droppedReferences =
                    await reloadParticipant
                        .PrepareReloadAsync(replacementManager, ct)
                        .ConfigureAwait(false);
                droppedInboundReferences = [.. droppedReferences];
                await m_server
                    .RefreshComplexTypesAsync(server, replacementManager, cancellationToken: ct)
                    .ConfigureAwait(false);
                ServerBindings bindings = await BindToServerAsync(
                    server,
                    replacementManager,
                    callerContext,
                    ct).ConfigureAwait(false);

                await host
                    .ReplaceAsync(
                        current.Prepared.NodeManager,
                        replacement,
                        allowActiveMonitoredItems: allowActiveMonitoredItems,
                        retainReplacedNotifications:
                            deferForActiveMonitoredItems,
                        ct: ct)
                    .ConfigureAwait(false);
                await CommitWithReconciliationAsync(
                    server,
                    host,
                    replacement,
                    replacementManager,
                    bindings,
                    callerContext,
                    ct,
                    beforeCommit: beforeReloadCommit,
                    afterCommit: afterTransitionCommit,
                    rollbackCommit: rollbackTransitionCommit).ConfigureAwait(false);
                RebuildTypeTree(replacementManager);
                current.Prepared.Published = false;
                var nextRegistration = new NodeManagerRegistration(
                    current.Registration.Id,
                    current.Registration.Generation + 1,
                    replacement.NodeManager);
                readinessState = new RegistrationState(
                    nextRegistration,
                    replacement,
                    allowRequestCallback)
                {
                    ReadinessPending = replacementManager is INodeManagerReadinessNotification
                };
                lock (m_registrationLock)
                {
                    m_registrations[current.Registration.Id] = readinessState;
                }

                var retired = new RetiredNodeManager(
                    current.Prepared.NodeManager,
                    droppedInboundReferences,
                    needsDetachment: true,
                    allowActiveMonitoredItems: deferForActiveMonitoredItems,
                    detachActiveMonitoredItems: retirementMode == ReloadRetirementMode.Immediate);
                lock (m_registrationLock)
                {
                    m_retiredNodeManagers.Add(retired);
                }

                // Register the drain observer so the host can trigger prompt cleanup once a
                // shadow-retired generation's retained uses drain, rather than waiting for
                // the next lifecycle operation or server shutdown.
                if (deferForActiveMonitoredItems)
                {
                    host.SetRetiredGenerationDrainObserver(
                        ScheduleRetiredGenerationDrainCleanup);
                }

                bool retiredDrainClaimed =
                    !deferForActiveMonitoredItems ||
                    !HasRetainedUses(
                        server,
                        retired.NodeManager);
                bool retiredDrainReady = retiredDrainClaimed;
                if (retiredDrainClaimed)
                {
                    if (deferForActiveMonitoredItems)
                    {
                        host.SetRetiredGenerationNotifications(
                            retired.NodeManager,
                            enabled: false);
                        retired.NotificationsSuspended = true;
                    }
                    retired.DrainPending = true;
                    try
                    {
                        await WaitForNotificationDispatchesOutsideLifecycleSemaphoreAsync(
                                server,
                                host,
                                retired.NodeManager)
                            .ConfigureAwait(false);
                        if (deferForActiveMonitoredItems &&
                            HasRetainedUses(server, retired.NodeManager))
                        {
                            host.SetRetiredGenerationNotifications(
                                retired.NodeManager,
                                enabled: true);
                            retired.NotificationsSuspended = false;
                            retired.DrainPending = false;
                            retiredDrainReady = false;
                        }
                        else if (!deferForActiveMonitoredItems)
                        {
                            InvalidateContinuationPoints(server, retired.NodeManager);
                        }
                    }
                    catch
                    {
                        // A pending drain blocks all retired-generation cleanup, so release
                        // the claim before the committed-reload failure is reported.
                        try
                        {
                            RestoreRetiredNotificationsForActiveItems(
                                server,
                                host,
                                retired);
                        }
                        finally
                        {
                            retired.DrainPending = false;
                        }
                        throw;
                    }
                }

                Exception? postCommitFailure = null;
                try
                {
                    await DrainRequestsOutsideLifecycleSemaphoreAsync(
                            server,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    EnsureSameRunningServer(
                        server,
                        host,
                        allowRequestCallback);
                    if (IsCurrentRegistration(nextRegistration))
                    {
                        await ReconcileBindingsAsync(
                            server,
                            replacementManager,
                            bindings,
                            callerContext,
                            CancellationToken.None).ConfigureAwait(false);
                        if (!allowActiveMonitoredItems)
                        {
                            await MigrateLateMonitoredItemsAsync(
                                server,
                                retired.NodeManager,
                                replacementManager).ConfigureAwait(false);
                        }
                    }

                    if (retiredDrainReady)
                    {
                        retired.RequestsDrained = true;
                        if (deferForActiveMonitoredItems &&
                            HasRetainedUses(
                                server,
                                retired.NodeManager))
                        {
                            host.SetRetiredGenerationNotifications(
                                retired.NodeManager,
                                enabled: true);
                            retired.NotificationsSuspended = false;
                            retired.RequestsDrained = false;
                        }
                        else
                        {
                            bool cleaned = await CleanupRetiredNodeManagerAsync(
                                server,
                                host,
                                retired).ConfigureAwait(false);
                            if (cleaned)
                            {
                                RemoveRetiredNodeManagerRecord(retired);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    postCommitFailure = ex;
                }
                finally
                {
                    if (retiredDrainClaimed)
                    {
                        try
                        {
                            RestoreRetiredNotificationsForActiveItems(
                                server,
                                host,
                                retired);
                        }
                        finally
                        {
                            retired.DrainPending = false;
                        }
                    }
                }

                try
                {
                    await CompleteReadinessOutsideLifecycleSemaphoreAsync(
                        server, host, readinessState, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    postCommitFailure = postCommitFailure is null
                        ? ex
                        : new AggregateException(postCommitFailure, ex);
                }

                if (!m_shuttingDown && !m_disposed)
                {
                    try
                    {
                        await NotifyCommittedChangeAsync(
                            server,
                            retirementMode switch
                            {
                                ReloadRetirementMode.Graceful => "shadow-reloaded",
                                ReloadRetirementMode.Immediate => "immediate-reloaded",
                                _ => "reloaded"
                            },
                            namespaceCountBefore,
                            CancellationToken.None,
                            semanticChanges).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        postCommitFailure = postCommitFailure is null
                            ? ex
                            : new AggregateException(postCommitFailure, ex);
                    }
                }

                if (postCommitFailure is not null)
                {
                    throw new NodeManagerReloadCommittedException(
                        nextRegistration,
                        "The replacement NodeManager is live, but reload completion failed. " +
                        "A later lifecycle operation will retry retired-generation cleanup.",
                        postCommitFailure);
                }
                return nextRegistration;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (ex is NodeManagerReloadCommittedException)
                {
                    throw;
                }

                Exception? cleanupException = null;
                if (replacement is not null &&
                    !replacement.Published &&
                    host is not null)
                {
                    cleanupException = await CleanupPreparedAsync(
                        server,
                        host,
                        replacement,
                        allowRequestCallback).ConfigureAwait(false);
                }

                NodeManagerRegistration? retainedRegistration = null;
                NodeManagerRegistration? committedRegistration = null;
                Exception? recoveryException = null;
                if (replacement?.Published == true &&
                    replacementManager is not null &&
                    current is not null &&
                    server is not null &&
                    host is not null)
                {
                    bool registrationAlreadyUpdated;
                    lock (m_registrationLock)
                    {
                        registrationAlreadyUpdated = m_registrations.TryGetValue(
                            current.Registration.Id,
                            out RegistrationState? retainedState) &&
                            ReferenceEquals(
                                retainedState.Registration.NodeManager,
                                replacementManager);
                        if (registrationAlreadyUpdated)
                        {
                            committedRegistration = retainedState!.Registration;
                        }
                    }

                    if (!registrationAlreadyUpdated)
                    {
                        retainedRegistration = new NodeManagerRegistration(
                            current.Registration.Id,
                            current.Registration.Generation + 1,
                            replacementManager);
                        lock (m_registrationLock)
                        {
                            m_registrations[current.Registration.Id] =
                                new RegistrationState(
                                    retainedRegistration,
                                    replacement,
                                    allowRequestCallback);
                            m_retiredNodeManagers.Add(
                                new RetiredNodeManager(
                                    current.Prepared.NodeManager,
                                    droppedInboundReferences,
                                    needsDetachment: true,
                                    allowActiveMonitoredItems: deferForActiveMonitoredItems,
                                    detachActiveMonitoredItems:
                                        retirementMode == ReloadRetirementMode.Immediate));
                        }

                        if (CanRecoverRunningServer(server, host))
                        {
                            try
                            {
                                ServerBindings recoveryBindings =
                                    await BindToServerAsync(
                                        server,
                                        replacementManager,
                                        callerContext,
                                        CancellationToken.None).ConfigureAwait(false);
                                await ReconcileBindingsAsync(
                                    server,
                                    replacementManager,
                                    recoveryBindings,
                                    callerContext,
                                    CancellationToken.None).ConfigureAwait(false);
                                await DrainRequestsOutsideLifecycleSemaphoreAsync(
                                        server,
                                        CancellationToken.None)
                                    .ConfigureAwait(false);
                                EnsureSameRunningServer(
                                    server,
                                    host,
                                    allowRequestCallback);
                                if (IsCurrentRegistration(retainedRegistration))
                                {
                                    await ReconcileBindingsAsync(
                                        server,
                                        replacementManager,
                                        recoveryBindings,
                                        callerContext,
                                        CancellationToken.None).ConfigureAwait(false);
                                }
                            }
                            catch (Exception recoveryFailure) when (
                                recoveryFailure is not OutOfMemoryException)
                            {
                                recoveryException = recoveryFailure;
                            }
                        }
                    }
                }

                if (committedRegistration is not null)
                {
                    throw new NodeManagerReloadCommittedException(
                        committedRegistration,
                        "The replacement NodeManager is live, but reload completion failed. " +
                        "A later lifecycle operation will retry retired-generation cleanup.",
                        ex);
                }

                if (server is not null && !m_disposed && !m_shuttingDown)
                {
                    if (replacementManager is not null)
                    {
                        RebuildActiveTypeTree(server);
                    }
                    await NotifyNamespaceTableChangedAsync(
                        server,
                        namespaceCountBefore,
                        CancellationToken.None).ConfigureAwait(false);
                }

                Exception? disposeException = null;
                if (replacementManager is not null &&
                    replacement?.Published != true &&
                    ex is not NodeManagerAlreadyRegisteredException)
                {
                    disposeException = await TryDisposeNodeManagerAsync(
                        replacementManager).ConfigureAwait(false);
                }
                if (retainedRegistration is not null)
                {
                    var failures = new List<Exception> { ex };
                    if (cleanupException is not null)
                    {
                        failures.Add(cleanupException);
                    }
                    if (recoveryException is not null)
                    {
                        failures.Add(recoveryException);
                    }
                    throw new NodeManagerReloadCommittedException(
                        retainedRegistration,
                        "NodeManager reload failed during rollback. " +
                        "The replacement generation was retained and is available " +
                        "from Registrations for retry or removal.",
                        new AggregateException(failures));
                }
                if (cleanupException is not null || disposeException is not null)
                {
                    var failures = new List<Exception> { ex };
                    if (cleanupException is not null)
                    {
                        failures.Add(cleanupException);
                    }
                    if (disposeException is not null)
                    {
                        failures.Add(disposeException);
                    }
                    throw new AggregateException(
                        "NodeManager reload preparation and cleanup failed.",
                        failures);
                }
                throw;
            }
            finally
            {
                ReleaseReadinessClaim(readinessState);
                m_lifecycleSemaphore.Release();
            }
        }

        /// <summary>
        /// Notifies a registered node manager that the server is ready while the lifecycle semaphore is released, then
        /// reacquires the semaphore and revalidates the registration.
        /// </summary>
        /// <param name="server">The running server.</param>
        /// <param name="host">The dynamic node manager host.</param>
        /// <param name="state">The registration being completed.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <returns>A task that completes when the readiness callback finished and the registration is still
        /// current.</returns>
        private async ValueTask CompleteReadinessOutsideLifecycleSemaphoreAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            RegistrationState state,
            CancellationToken ct)
        {
            if (state.Prepared.NodeManager is not INodeManagerReadinessNotification participant)
            {
                return;
            }

            m_lifecycleSemaphore.Release();
            try
            {
                ct.ThrowIfCancellationRequested();
                await participant.OnServerReadyAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
            finally
            {
                await m_lifecycleSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            EnsureSameRunningServer(server, host, state.AllowLifecycleFromRequestCallback);
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
        private void ReleaseReadinessClaim(RegistrationState? state)
        {
            if (state is not null)
            {
                lock (m_registrationLock)
                {
                    state.ReadinessPending = false;
                }
            }
        }

        /// <summary>
        /// Destroys the address space of a detached node manager while the lifecycle semaphore is released, then
        /// reacquires the semaphore.
        /// </summary>
        /// <param name="host">The dynamic node manager host.</param>
        /// <param name="nodeManager">The detached node manager.</param>
        /// <param name="ct">The token used to cancel the operation.</param>
        /// <returns>A task that completes when the address space was destroyed.</returns>
        private async ValueTask DestroyAddressSpaceOutsideLifecycleSemaphoreAsync(
            IDynamicNodeManagerHost host,
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            // The removal or retired-drain claim retains this detached generation while
            // its deletion callback releases dependencies through the same lifecycle.
            m_lifecycleSemaphore.Release();
            try
            {
                await host.DestroyAddressSpaceAsync(nodeManager, ct).ConfigureAwait(false);
            }
            finally
            {
                await m_lifecycleSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private (IServerInternal Server, IDynamicNodeManagerHost Host) GetRunningServer(
            bool allowRequestCallback = false,
            IOperationContext? callerContext = null)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
            }
            if (m_shuttingDown)
            {
                throw new InvalidOperationException(
                    "The NodeManager lifecycle is shutting down.");
            }

            if (m_server.CurrentState is not (ServerState.Running or ServerState.NoConfiguration))
            {
                throw new InvalidOperationException(
                    "NodeManagers can only be changed while the server is running.");
            }

            IServerInternal server = m_server.CurrentInstance;
            if (server.RequestManager.IsExecutingRequest(callerContext) &&
                !allowRequestCallback)
            {
                throw new InvalidOperationException(
                    "NodeManager lifecycle operations cannot run from an OPC UA request callback.");
            }
            if (server.NodeManager is not IDynamicNodeManagerHost host)
            {
                throw new NotSupportedException(
                    "The configured master NodeManager does not support live lifecycle operations.");
            }
            if (host is MasterNodeManager master)
            {
                master.SetHistoryContinuationLifecycle(this);
            }
            return (server, host);
        }

        /// <summary>
        /// Determines whether the specified node manager is a registered or retired history source owned by this
        /// lifecycle.
        /// </summary>
        /// <param name="nodeManager">The node manager to look up.</param>
        /// <returns>True if the lifecycle owns the node manager.</returns>
        internal bool OwnsHistorySource(IAsyncNodeManager nodeManager)
        {
            lock (m_registrationLock)
            {
                return m_registrations.Values.Any(
                    state => ReferenceEquals(state.Registration.NodeManager, nodeManager)) ||
                    m_retiredNodeManagers.Exists(state => ReferenceEquals(state.NodeManager, nodeManager));
            }
        }

        /// <summary>
        /// Enters a lifecycle operation that is tracked until it is disposed.
        /// </summary>
        /// <param name="publication">The publication capture that owns admission, or null for a regular
        /// operation.</param>
        /// <returns>The scope that ends the operation when disposed.</returns>
        private OperationLifetime EnterLifecycleOperation(PublicationCapture? publication = null)
        {
            lock (m_operationLifetimeLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
                }
                if (m_shuttingDown)
                {
                    throw new InvalidOperationException(
                        "The NodeManager lifecycle is shutting down.");
                }
                if (m_publicationCapture is not null &&
                    !ReferenceEquals(m_publicationCapture, publication))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadServerTooBusy, "An invocation owns lifecycle publication admission.");
                }
                if (publication is not null && !ReferenceEquals(m_publicationCapture, publication))
                {
                    throw new ObjectDisposedException(nameof(INodeManagerPublication));
                }

                m_activeLifecycleOperations++;
                var operation = new OperationLifetime(this, m_currentOperation.Value);
                m_currentOperation.Value = operation;
                return operation;
            }
        }

        private bool TryEnterBackgroundOperation()
        {
            lock (m_operationLifetimeLock)
            {
                if (m_disposed || m_shuttingDown || m_publicationCapture is not null)
                {
                    return false;
                }

                m_activeLifecycleOperations++;
                return true;
            }
        }

        private void ExitLifecycleOperation()
        {
            TaskCompletionSource<bool>? operationsDrained = null;
            bool disposeSemaphore;
            lock (m_operationLifetimeLock)
            {
                if (--m_activeLifecycleOperations == 0)
                {
                    operationsDrained = m_operationsDrained;
                    m_operationsDrained = null;
                }
                disposeSemaphore = TryReserveSemaphoreDisposal();
            }

            operationsDrained?.TrySetResult(true);
            if (disposeSemaphore)
            {
                m_lifecycleSemaphore.Dispose();
            }
        }

        private Task EnterShutdownMethod()
        {
            lock (m_operationLifetimeLock)
            {
                if (m_lifecycleSemaphoreDisposed)
                {
                    throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
                }

                m_shuttingDown = true;
                m_activeShutdownMethods++;
                if (m_activeLifecycleOperations == 0)
                {
                    return Task.CompletedTask;
                }
                return (m_operationsDrained ??=
                    new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
        }

        private void ExitBeginShutdownMethod(bool shutdownPrepared)
        {
            bool disposeSemaphore;
            lock (m_operationLifetimeLock)
            {
                if (shutdownPrepared)
                {
                    m_shutdownPrepared = true;
                }
                m_activeShutdownMethods--;
                disposeSemaphore = TryReserveSemaphoreDisposal();
            }
            if (disposeSemaphore)
            {
                m_lifecycleSemaphore.Dispose();
            }
        }

        private void ExitCompleteShutdownMethod(bool shutdownCompleted)
        {
            bool disposeSemaphore;
            lock (m_operationLifetimeLock)
            {
                m_shutdownPrepared = !shutdownCompleted;
                m_activeShutdownMethods--;
                disposeSemaphore = TryReserveSemaphoreDisposal();
            }
            if (disposeSemaphore)
            {
                m_lifecycleSemaphore.Dispose();
            }
        }

        private bool TryReserveSemaphoreDisposal()
        {
            if (!m_disposeRequested ||
                m_lifecycleSemaphoreDisposed ||
                m_activeLifecycleOperations != 0 ||
                m_activeShutdownMethods != 0 ||
                m_shutdownPrepared)
            {
                return false;
            }

            m_lifecycleSemaphoreDisposed = true;
            return true;
        }

        private bool CanRecoverRunningServer(
            IServerInternal server,
            IDynamicNodeManagerHost host)
        {
            // Linearize the decision to start rollback recovery with shutdown intent.
            lock (m_operationLifetimeLock)
            {
                return !m_disposed &&
                    !m_shuttingDown &&
                    m_server.CurrentState is (ServerState.Running or ServerState.NoConfiguration) &&
                    ReferenceEquals(m_server.CurrentInstance, server) &&
                    ReferenceEquals(server.NodeManager, host);
            }
        }

        /// <summary>
        /// Releases the lifecycle semaphore for a request drain and always reacquires it
        /// before returning. Callers must revalidate every server or registration state
        /// that can change while the semaphore is released.
        /// </summary>
        private async ValueTask DrainRequestsOutsideLifecycleSemaphoreAsync(
            IServerInternal server,
            CancellationToken ct)
        {
            m_lifecycleSemaphore.Release();
            try
            {
                await server.RequestManager
                    .WaitForCurrentRequestsAsync(ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                await m_lifecycleSemaphore
                    .WaitAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        private async ValueTask FinalizeNotificationsOutsideLifecycleSemaphoreAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            IAsyncNodeManager nodeManager,
            bool allowShuttingDown = false)
        {
            m_lifecycleSemaphore.Release();
            try
            {
                await host
                    .FinalizeRetiredGenerationNotificationsAsync(
                        nodeManager,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await m_lifecycleSemaphore
                    .WaitAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (!TryGetRunningServer(
                out IServerInternal currentServer,
                out IDynamicNodeManagerHost currentHost,
                allowShuttingDown) ||
                !ReferenceEquals(currentServer, server) ||
                !ReferenceEquals(currentHost, host))
            {
                throw new InvalidOperationException(
                    "The running server changed while notification dispatches drained.");
            }
        }

        private async ValueTask WaitForNotificationDispatchesOutsideLifecycleSemaphoreAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            IAsyncNodeManager nodeManager,
            bool allowShuttingDown = false)
        {
            m_lifecycleSemaphore.Release();
            try
            {
                await host
                    .WaitForNotificationDispatchesAsync(
                        nodeManager,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await m_lifecycleSemaphore
                    .WaitAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (!TryGetRunningServer(
                out IServerInternal currentServer,
                out IDynamicNodeManagerHost currentHost,
                allowShuttingDown) ||
                !ReferenceEquals(currentServer, server) ||
                !ReferenceEquals(currentHost, host))
            {
                throw new InvalidOperationException(
                    "The running server changed while notification dispatches drained.");
            }
        }

        private void EnsureSameRunningServer(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            bool allowRequestCallback)
        {
            (IServerInternal currentServer, IDynamicNodeManagerHost currentHost) =
                GetRunningServer(allowRequestCallback);
            if (!ReferenceEquals(currentServer, server) ||
                !ReferenceEquals(currentHost, host))
            {
                throw new InvalidOperationException(
                    "The running server changed while requests were draining.");
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
                    m_retiredNodeManagers.Any(retired =>
                        AreSameNodeManager(
                            retired.NodeManager,
                            nodeManager));
            }
        }

        private void EnsureRequestCallbackAllowed(
            bool allowRequestCallback,
            IOperationContext? callerContext)
        {
            // The server state is deliberately not consulted. A request that is still executing
            // while the server shuts down would otherwise slip past the guard and wait for its
            // own request, and the request registry reports an exact answer in every state.
            if (m_server.CurrentInstance.RequestManager.IsExecutingRequest(callerContext) &&
                !allowRequestCallback)
            {
                throw new InvalidOperationException(
                    "NodeManager lifecycle operations cannot run from an OPC UA request callback.");
            }
        }

        private async ValueTask WaitForLifecycleSemaphoreAsync(
            RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? requestWaiter,
            CancellationToken ct)
        {
            Task semaphoreWait = m_lifecycleSemaphore.WaitAsync(ct);
            requestWaiter?.MarkSemaphoreWaitStarted();
            await semaphoreWait.ConfigureAwait(false);
        }

        private static RequestManagerLifecycleExtension.RequestLifecycleWaiterScope?
            EnterRequestLifecycleWaiter(IServerInternal server, IOperationContext? callerContext = null)
        {
            RequestManagerLifecycleExtension? extension =
                server.RequestManager.LifecycleExtension;
            if (extension is null)
            {
                return null;
            }

            // An executing request named by the explicit caller context is the one to exclude from its own
            // drain. Callers without such a context (ShadowReloadAsync and other internal callers) still run
            // inside a request scope, so the ambient request keeps them excluded.
            if (callerContext is OperationContext operation &&
                server.RequestManager.IsExecutingRequest(operation))
            {
                return extension.EnterLifecycleWaiter(operation.RequestId);
            }
            uint? currentRequestId =
                server.RequestManager.GetCurrentRequestIdForLifecycleExtension();
            return currentRequestId.HasValue && currentRequestId.Value != uint.MaxValue
                ? extension.EnterLifecycleWaiter()
                : null;
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

        private static bool IsRequestCallbackSafe(IAsyncNodeManagerFactory factory)
        {
            return factory is IRequestCallbackSafeNodeManagerFactory
            {
                AllowLifecycleFromRequestCallback: true
            };
        }

        private RegistrationState GetCurrentState(
            NodeManagerRegistration registration,
            bool allowRemovalRetry = false)
        {
            lock (m_registrationLock)
            {
                if (!m_registrations.TryGetValue(
                    registration.Id,
                    out RegistrationState? state) ||
                    state.Registration.Generation != registration.Generation ||
                    !ReferenceEquals(
                        state.Registration.NodeManager,
                        registration.NodeManager) ||
                    state.RemovalPending ||
                    (state.ShutdownCleanup.RemovalUnpublished &&
                        !allowRemovalRetry))
                {
                    throw new InvalidOperationException(
                        "The registration is stale or is not owned by this lifecycle provider.");
                }
                if (state.ReadinessPending)
                {
                    throw new InvalidOperationException(
                        "The NodeManager is completing readiness. Retry after its startup operation completes.");
                }
                return state;
            }
        }

        private void ClaimRemoval(
            NodeManagerRegistration registration,
            RegistrationState state)
        {
            lock (m_registrationLock)
            {
                if (!m_registrations.TryGetValue(
                        registration.Id,
                        out RegistrationState? currentState) ||
                    !ReferenceEquals(currentState, state) ||
                    state.RemovalPending)
                {
                    throw new InvalidOperationException(
                        "The registration changed while removal was being committed.");
                }
                state.RemovalPending = true;
            }
        }

        private void ValidateRemovalClaim(
            NodeManagerRegistration registration,
            RegistrationState state,
            string message)
        {
            lock (m_registrationLock)
            {
                if (!m_registrations.TryGetValue(
                        registration.Id,
                        out RegistrationState? currentState) ||
                    !ReferenceEquals(currentState, state) ||
                    !state.RemovalPending)
                {
                    throw new InvalidOperationException(message);
                }
            }
        }

        private static void MarkRemovalUnpublished(ShutdownCleanupState cleanup)
        {
            cleanup.RemovalUnpublished = true;
            cleanup.ReferencesRemoved = true;
        }

        private static void ResetRemovalUnpublished(ShutdownCleanupState cleanup)
        {
            cleanup.RemovalUnpublished = false;
            cleanup.ReferencesRemoved = false;
        }

        private static void MarkRemovalDetached(
            ShutdownCleanupState cleanup,
            MonitoredItemTransition monitoredItemTransition)
        {
            if (!cleanup.RemovalMonitoredItemsDeleted)
            {
                monitoredItemTransition.MarkDeletedItems();
                cleanup.RemovalMonitoredItemsDeleted = true;
            }
            cleanup.Detached = true;
        }

        private static async ValueTask<MonitoredItemTransition>
            PrepareMonitoredItemRemovalAsync(
                IServerInternal server,
                IAsyncNodeManager nodeManager,
                CancellationToken ct)
        {
            (INodeManagerMonitoredItemLifecycle lifecycle, IReadOnlyList<IMonitoredItem> items) =
                await GetOwnedMonitoredItemsAsync(server, nodeManager, ct)
                    .ConfigureAwait(false);
            return new MonitoredItemTransition(
                server,
                lifecycle,
                replacement: null,
                compatibleItems: [],
                deletedItems: items);
        }

        private static async ValueTask DetachActiveMonitoredItemsAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            MonitoredItemTransition monitoredItemTransition =
                await PrepareMonitoredItemRemovalAsync(
                    server,
                    nodeManager,
                    CancellationToken.None).ConfigureAwait(false);
            await monitoredItemTransition
                .DetachCurrentAsync(CancellationToken.None)
                .ConfigureAwait(false);
            monitoredItemTransition.MarkDeletedItems();
        }

        /// <summary>
        /// Moves monitored items that in-flight requests created on a migrated generation
        /// after its commit (their routing snapshot still contained it) to the replacement.
        /// </summary>
        private static async ValueTask MigrateLateMonitoredItemsAsync(
            IServerInternal server,
            IAsyncNodeManager retired,
            IAsyncNodeManager replacement)
        {
            if (!HasActiveMonitoredItems(server, retired))
            {
                return;
            }

            MonitoredItemTransition monitoredItemTransition =
                await PrepareMonitoredItemTransitionAsync(
                    server,
                    retired,
                    replacement,
                    CancellationToken.None).ConfigureAwait(false);
            await monitoredItemTransition
                .DetachCurrentAsync(CancellationToken.None)
                .ConfigureAwait(false);
            List<Exception> failures =
                await monitoredItemTransition.AttachCompatibleAsync(
                    CancellationToken.None).ConfigureAwait(false);
            monitoredItemTransition.MarkDeletedItems();
            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Monitored items created during the reload could not be migrated.",
                    failures);
            }
        }

        private static async ValueTask<MonitoredItemTransition>
            PrepareMonitoredItemTransitionAsync(
                IServerInternal server,
                IAsyncNodeManager current,
                IAsyncNodeManager replacement,
                CancellationToken ct)
        {
            (INodeManagerMonitoredItemLifecycle currentLifecycle,
                IReadOnlyList<IMonitoredItem> items) =
                await GetOwnedMonitoredItemsAsync(server, current, ct)
                    .ConfigureAwait(false);
            if (items.Count == 0)
            {
                return new MonitoredItemTransition(
                    server,
                    currentLifecycle,
                    replacement: null,
                    compatibleItems: [],
                    deletedItems: []);
            }
            if (replacement is not INodeManagerMonitoredItemLifecycle replacementLifecycle)
            {
                throw new NotSupportedException(
                    "The replacement NodeManager does not support monitored-item transitions.");
            }

            var compatibleItems = new List<IMonitoredItem>(items.Count);
            var deletedItems = new List<IMonitoredItem>();
            foreach (IMonitoredItem monitoredItem in items)
            {
                ServiceResult result = await replacementLifecycle
                    .CanAttachMonitoredItemAsync(monitoredItem, ct)
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
                server,
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

        private static async ValueTask<(
            INodeManagerMonitoredItemLifecycle Lifecycle,
            IReadOnlyList<IMonitoredItem> Items)> GetOwnedMonitoredItemsAsync(
                IServerInternal server,
                IAsyncNodeManager nodeManager,
                CancellationToken ct)
        {
            var subscriptionItems = new List<IMonitoredItem>();
            foreach (ISubscription subscription in server.SubscriptionManager.GetSubscriptions())
            {
                if (subscription.MonitoredItemCount == 0)
                {
                    continue;
                }
                if (subscription is not ISubscriptionMonitoredItemLifecycle tracker)
                {
                    throw new NotSupportedException(
                        "The configured subscription cannot verify NodeManager ownership.");
                }
                if (!tracker.HasMonitoredItems(nodeManager))
                {
                    continue;
                }
                if (subscription is not ISubscriptionMonitoredItemLifecycle lifecycle)
                {
                    throw new NotSupportedException(
                        "The configured subscription does not support monitored-item transitions.");
                }
                subscriptionItems.AddRange(lifecycle.GetMonitoredItemsSnapshot(nodeManager));
            }

            if (nodeManager is not INodeManagerMonitoredItemLifecycle nodeManagerLifecycle)
            {
                if (subscriptionItems.Count > 0)
                {
                    throw new NotSupportedException(
                        "The NodeManager does not support monitored-item transitions.");
                }
                return (UnsupportedMonitoredItemLifecycle.Instance, []);
            }

            IReadOnlyList<IMonitoredItem> managerItems =
                await nodeManagerLifecycle.GetMonitoredItemsSnapshotAsync(
                    cancellationToken: ct).ConfigureAwait(false);
            var ownedManagerItems = managerItems
                .Where(item =>
                    (item.MonitoredItemType & MonitoredItemTypeMask.AllEvents) == 0 &&
                    item is not IDetachableMonitoredItem
                    {
                        IsDetached: true
                    } &&
                    AreSameNodeManager(item.NodeManager, nodeManager))
                .ToList();
            if (ownedManagerItems.Count != subscriptionItems.Count ||
                ownedManagerItems.Any(managerItem =>
                    !subscriptionItems.Any(subscriptionItem =>
                        ReferenceEquals(subscriptionItem, managerItem))))
            {
                throw new NotSupportedException(
                    "The subscription and NodeManager monitored-item ownership snapshots do not match.");
            }
            return (nodeManagerLifecycle, subscriptionItems);
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

        private static ValueTask RecoverDetachedMonitoredItemsAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            return server.NodeManager is IDynamicNodeManagerHost recovery
                ? recovery.RecoverDetachedMonitoredItemsAsync(
                    nodeManager,
                    cancellationToken: ct)
                : default;
        }

        private static void EnsureNoActiveMonitoredItems(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            if (HasActiveMonitoredItems(server, nodeManager))
            {
                throw new InvalidOperationException(
                    "The NodeManager cannot be reloaded or removed while it owns monitored items.");
            }
        }

        private static bool HasActiveMonitoredItems(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            foreach (ISubscription subscription in server.SubscriptionManager.GetSubscriptions())
            {
                if (subscription.MonitoredItemCount == 0)
                {
                    continue;
                }
                if (subscription is not ISubscriptionMonitoredItemLifecycle tracker)
                {
                    throw new NotSupportedException(
                        "The configured subscription cannot verify NodeManager ownership.");
                }
                if (tracker.HasMonitoredItems(nodeManager))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether a retired node manager is still used by monitored items or by Browse and HistoryRead
        /// continuation points of any session.
        /// </summary>
        /// <param name="server">The running server.</param>
        /// <param name="nodeManager">The retired node manager.</param>
        /// <returns>True if the node manager is still retained.</returns>
        private bool HasRetainedUses(IServerInternal server, IAsyncNodeManager nodeManager)
        {
            if (HasActiveMonitoredItems(server, nodeManager))
            {
                return true;
            }
            foreach (ISession session in server.SessionManager.GetSessions())
            {
                if (session.ContinuationPoints is not ISessionContinuationPointLifecycle owner)
                {
                    throw new NotSupportedException(
                        "The configured session cannot verify Browse continuation ownership.");
                }
                owner.BrowseContinuationPointsReleased -= ScheduleRetiredGenerationDrainCleanup;
                owner.BrowseContinuationPointsReleased += ScheduleRetiredGenerationDrainCleanup;
                if (session.ContinuationPoints is not ISessionHistoryContinuationPointLifecycle historyOwner)
                {
                    throw new NotSupportedException(
                        "The configured session cannot verify HistoryRead continuation ownership.");
                }
                historyOwner.HistoryContinuationPointsReleased -= ScheduleRetiredGenerationDrainCleanup;
                historyOwner.HistoryContinuationPointsReleased += ScheduleRetiredGenerationDrainCleanup;
                if (historyOwner.HasHistoryForManager(nodeManager))
                {
                    return true;
                }
                if (owner.HasBrowseForManager(nodeManager))
                {
                    return true;
                }
            }
            return false;
        }

        private static void InvalidateContinuationPoints(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            foreach (ISession session in server.SessionManager.GetSessions())
            {
                session.ContinuationPoints.RemoveForManager(nodeManager);
            }
        }

        private static async ValueTask CommitWithReconciliationAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            PreparedNodeManager prepared,
            IAsyncNodeManager nodeManager,
            ServerBindings bindings,
            IOperationContext? callerContext,
            CancellationToken ct,
            Func<ValueTask>? beforeCommit = null,
            Func<ValueTask>? afterCommit = null,
            Func<ValueTask>? rollbackCommit = null)
        {
            try
            {
                await host.CommitAsync(
                    prepared,
                    async () =>
                    {
                        if (server is INodeIdFactoryProvider { NodeIdFactory: INodeIdFactoryPolicy policy })
                        {
                            await policy.PrepareNodeManagerAsync(
                                server, nodeManager, prepared.ReplacedNodeManager, ct).ConfigureAwait(false);
                        }
                        if (beforeCommit is not null)
                        {
                            await beforeCommit().ConfigureAwait(false);
                        }
                        await ReconcileBindingsAsync(
                            server,
                            nodeManager,
                            bindings,
                            callerContext,
                            ct).ConfigureAwait(false);
                    },
                    async () =>
                    {
                        await RebindIdentityAsync(server, ct).ConfigureAwait(false);
                        if (afterCommit is not null)
                        {
                            await afterCommit().ConfigureAwait(false);
                        }
                    },
                    rollbackCommit,
                    ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                try
                {
                    await RebindIdentityAsync(server, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception recoveryException) when (recoveryException is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "NodeManager publication and identity-binding recovery both failed.",
                        exception,
                        recoveryException);
                }
                throw;
            }
        }

        private static ValueTask RebindIdentityAsync(IServerInternal server, CancellationToken cancellationToken)
        {
            return server is INodeIdFactoryProvider { NodeIdFactory: INodeIdFactoryPolicy policy }
                ? policy.OnNodeManagersChangedAsync(server, cancellationToken)
                : default;
        }

        private static async ValueTask<ServerBindings> BindToServerAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            IOperationContext? callerContext,
            CancellationToken ct)
        {
            var bindings = new ServerBindings();
            try
            {
                foreach (ISession session in server.SessionManager.GetSessions())
                {
                    if (!session.Activated ||
                        session.IsClosing)
                    {
                        continue;
                    }

                    SessionBinding? binding = await ActivateSessionAsync(
                        server,
                        nodeManager,
                        session,
                        ct).ConfigureAwait(false);
                    if (binding is not null)
                    {
                        bindings.Sessions[session.Id] = binding;
                    }
                }

                foreach (IEventMonitoredItem monitoredItem in server.EventManager.GetMonitoredItems())
                {
                    if (!monitoredItem.MonitoringAllEvents)
                    {
                        continue;
                    }

                    if (await SubscribeToAllEventsAsync(
                        server,
                        nodeManager,
                        monitoredItem,
                        callerContext,
                        ct).ConfigureAwait(false))
                    {
                        bindings.EventMonitoredItems[monitoredItem.Id] = monitoredItem;
                    }
                }
                return bindings;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                try
                {
                    await UnbindBindingsAsync(
                        nodeManager,
                        bindings,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cleanupException) when (
                    cleanupException is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "NodeManager binding and cleanup both failed.",
                        ex,
                        cleanupException);
                }
                throw;
            }
        }

        private static async ValueTask ReconcileBindingsAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            ServerBindings bindings,
            IOperationContext? callerContext,
            CancellationToken ct)
        {
            ISession[] currentSessions =
            [
                .. server.SessionManager
                    .GetSessions()
                    .Where(session =>
                        session.Activated &&
                        !session.IsClosing)
            ];
            Dictionary<NodeId, ISession> currentSessionsById =
                currentSessions.ToDictionary(session => session.Id);
            foreach (KeyValuePair<NodeId, SessionBinding> binding in
                bindings.Sessions.ToArray())
            {
                if (currentSessionsById.TryGetValue(
                    binding.Key,
                    out ISession? currentSession) &&
                    ReferenceEquals(
                        currentSession,
                        binding.Value.Session))
                {
                    continue;
                }

                using var sessionContext = new OperationContext(
                    binding.Value.Session,
                    DiagnosticsMasks.None);
                await nodeManager
                    .SessionClosingAsync(
                        sessionContext,
                        binding.Key,
                        deleteSubscriptions: false,
                        ct)
                    .ConfigureAwait(false);
                bindings.Sessions.Remove(binding.Key);
            }

            foreach (ISession session in currentSessions)
            {
                if (bindings.Sessions.TryGetValue(
                    session.Id,
                    out SessionBinding? binding) &&
                    ReferenceEquals(
                        binding.Identity,
                        session.EffectiveIdentity))
                {
                    continue;
                }

                SessionBinding? newBinding = await ActivateSessionAsync(
                    server,
                    nodeManager,
                    session,
                    ct).ConfigureAwait(false);
                if (newBinding is not null)
                {
                    bindings.Sessions[session.Id] = newBinding;
                }
            }

            IList<IEventMonitoredItem> currentEventMonitoredItems =
                server.EventManager.GetMonitoredItems();
            var currentEventsById =
                currentEventMonitoredItems.ToDictionary(
                    monitoredItem => monitoredItem.Id);
            foreach (KeyValuePair<uint, IEventMonitoredItem> binding in
                bindings.EventMonitoredItems.ToArray())
            {
                if (currentEventsById.TryGetValue(
                    binding.Key,
                    out IEventMonitoredItem? currentMonitoredItem) &&
                    ReferenceEquals(
                        currentMonitoredItem,
                        binding.Value))
                {
                    continue;
                }

                using var eventContext = new OperationContext(binding.Value);
                await nodeManager
                    .SubscribeToAllEventsAsync(
                        eventContext,
                        binding.Value.SubscriptionId,
                        binding.Value,
                        true,
                        ct)
                    .ConfigureAwait(false);
                bindings.EventMonitoredItems.Remove(binding.Key);
            }

            foreach (IEventMonitoredItem monitoredItem in currentEventMonitoredItems)
            {
                if (!monitoredItem.MonitoringAllEvents ||
                    bindings.EventMonitoredItems.ContainsKey(monitoredItem.Id))
                {
                    continue;
                }

                if (await SubscribeToAllEventsAsync(
                    server,
                    nodeManager,
                    monitoredItem,
                    callerContext,
                    ct).ConfigureAwait(false))
                {
                    bindings.EventMonitoredItems[monitoredItem.Id] = monitoredItem;
                }
            }
        }

        private static async ValueTask UnbindFromServerAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            CancellationToken ct,
            bool unsubscribeAllEvents = true)
        {
            if (unsubscribeAllEvents)
            {
                foreach (IEventMonitoredItem monitoredItem in
                    server.EventManager.GetMonitoredItems())
                {
                    if (!monitoredItem.MonitoringAllEvents)
                    {
                        continue;
                    }

                    using var eventContext = new OperationContext(monitoredItem);
                    await nodeManager
                        .SubscribeToAllEventsAsync(
                            eventContext,
                            monitoredItem.SubscriptionId,
                            monitoredItem,
                            true,
                            ct)
                        .ConfigureAwait(false);
                }
            }

            foreach (ISession session in server.SessionManager.GetSessions())
            {
                using var context = new OperationContext(session, DiagnosticsMasks.None);
                await nodeManager
                    .SessionClosingAsync(
                        context,
                        session.Id,
                        deleteSubscriptions: false,
                        ct)
                    .ConfigureAwait(false);
            }
        }

        private static async ValueTask UnbindBindingsAsync(
            IAsyncNodeManager nodeManager,
            ServerBindings bindings,
            CancellationToken ct)
        {
            foreach (IEventMonitoredItem monitoredItem in
                bindings.EventMonitoredItems.Values)
            {
                using var eventContext = new OperationContext(monitoredItem);
                await nodeManager
                    .SubscribeToAllEventsAsync(
                        eventContext,
                        monitoredItem.SubscriptionId,
                        monitoredItem,
                        true,
                        ct)
                    .ConfigureAwait(false);
            }

            foreach (KeyValuePair<NodeId, SessionBinding> binding in
                bindings.Sessions)
            {
                using var sessionContext = new OperationContext(
                    binding.Value.Session,
                    DiagnosticsMasks.None);
                await nodeManager
                    .SessionClosingAsync(
                        sessionContext,
                        binding.Key,
                        deleteSubscriptions: false,
                        ct)
                    .ConfigureAwait(false);
            }
        }

        private static async ValueTask<SessionBinding?> ActivateSessionAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            ISession session,
            CancellationToken ct)
        {
            while (true)
            {
                using var context = new OperationContext(session, DiagnosticsMasks.None);
                IUserIdentity identity = context.UserIdentity;
                try
                {
                    await nodeManager
                        .SessionActivatedAsync(context, session.Id, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    try
                    {
                        await nodeManager
                            .SessionClosingAsync(
                                context,
                                session.Id,
                                deleteSubscriptions: false,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception cleanupException) when (
                        cleanupException is not OutOfMemoryException)
                    {
                        throw new AggregateException(
                            "NodeManager session activation and cleanup both failed.",
                            ex,
                            cleanupException);
                    }
                    throw;
                }
                if (ReferenceEquals(identity, session.EffectiveIdentity) &&
                    session.Activated &&
                    !session.IsClosing &&
                    server.SessionManager.GetSessions().Any(current =>
                        ReferenceEquals(current, session)))
                {
                    return new SessionBinding(session, identity);
                }
                if (!session.Activated ||
                    session.IsClosing ||
                    !server.SessionManager.GetSessions().Any(current =>
                        ReferenceEquals(current, session)))
                {
                    await nodeManager
                        .SessionClosingAsync(
                            context,
                            session.Id,
                            deleteSubscriptions: false,
                            ct)
                        .ConfigureAwait(false);
                    return null;
                }
                ct.ThrowIfCancellationRequested();
            }
        }

        private static async ValueTask<bool> SubscribeToAllEventsAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            IEventMonitoredItem monitoredItem,
            IOperationContext? callerContext,
            CancellationToken ct)
        {
            // Only the explicit context of the lifecycle caller can identify a delete that is removing this item.
            if ((server.NodeManager as IDynamicNodeManagerBatchHost)?.IsRemovingBinding(
                callerContext, monitoredItem) == true)
            {
                return false;
            }
            using var context = new OperationContext(monitoredItem);
            try
            {
                ServiceResult result = await nodeManager
                    .SubscribeToAllEventsAsync(
                        context,
                        monitoredItem.SubscriptionId,
                        monitoredItem,
                        false,
                        ct)
                    .ConfigureAwait(false);
                if (ServiceResult.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                try
                {
                    ServiceResult cleanup = await nodeManager
                        .SubscribeToAllEventsAsync(
                            context,
                            monitoredItem.SubscriptionId,
                            monitoredItem,
                            true,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(cleanup))
                    {
                        throw new ServiceResultException(cleanup);
                    }
                }
                catch (Exception cleanupException) when (
                    cleanupException is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "NodeManager event binding and cleanup both failed.",
                        ex,
                        cleanupException);
                }
                throw;
            }

            if (monitoredItem.MonitoringAllEvents &&
                server.EventManager.GetMonitoredItems().Any(current =>
                    ReferenceEquals(current, monitoredItem)))
            {
                return true;
            }

            using var eventContext = new OperationContext(monitoredItem);
            await nodeManager
                .SubscribeToAllEventsAsync(
                    eventContext,
                    monitoredItem.SubscriptionId,
                    monitoredItem,
                    true,
                    ct)
                .ConfigureAwait(false);
            return false;
        }

        private static async ValueTask ValidateDataTypeCompatibilityAsync(
            IServerInternal server,
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
            IServerInternal server,
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

        private static void RebuildActiveTypeTree(IServerInternal server)
        {
            foreach (IAsyncNodeManager nodeManager in server.NodeManager.AsyncNodeManagers)
            {
                RebuildTypeTree(nodeManager);
            }
        }

        private static void RebuildTypeTree(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is AsyncCustomNodeManager asyncCustomNodeManager)
            {
                asyncCustomNodeManager.RebuildTypeTree();
            }
            else if (nodeManager.SyncNodeManager is CustomNodeManager2 customNodeManager)
            {
                customNodeManager.RebuildTypeTree();
            }
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

        private static async ValueTask NotifyCommittedChangeAsync(
            IServerInternal server,
            string operation,
            int namespaceCountBefore,
            CancellationToken ct,
            ArrayOf<SemanticChangeStructureDataType> semanticChanges = default)
        {
            await RebindIdentityAsync(server, ct).ConfigureAwait(false);
            await NotifyNamespaceTableChangedAsync(
                server,
                namespaceCountBefore,
                ct).ConfigureAwait(false);

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
            CancellationToken ct)
        {
            ServerObjectState serverObject =
                server.DiagnosticsNodeManager.FindPredefinedNode<ServerObjectState>(
                    ObjectIds.Server);
            if (server.NamespaceUris.Count != namespaceCountBefore)
            {
                serverObject.NamespaceArray?.UpdateChangeMasks(NodeStateChangeMasks.Value);
                if (serverObject.NamespaceArray is not null)
                {
                    await serverObject.NamespaceArray
                        .ClearChangeMasksAsync(
                            server.DefaultSystemContext,
                            includeChildren: false,
                            ct)
                        .ConfigureAwait(false);
                }

                if (serverObject.UrisVersion is not null)
                {
                    DateTimeUtc now = DateTimeUtc.Now;
                    uint version = serverObject.UrisVersion.Value;
                    serverObject.UrisVersion.Value =
                        Utils.IncrementIdentifier(ref version);
                    serverObject.UrisVersion.Timestamp = now;
                    serverObject.UrisVersion.UpdateChangeMasks(NodeStateChangeMasks.Value);
                    await serverObject.UrisVersion
                        .ClearChangeMasksAsync(
                            server.DefaultSystemContext,
                            includeChildren: false,
                            ct)
                        .ConfigureAwait(false);
                }
            }
        }

        private static async ValueTask DisposeNodeManagerAsync(
            IAsyncNodeManager nodeManager)
        {
            if (nodeManager is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (nodeManager is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private async ValueTask CleanupShutdownNodeManagerAsync(
            IServerInternal server,
            IDynamicNodeManagerHost? host,
            IAsyncNodeManager nodeManager,
            ShutdownCleanupState cleanup,
            List<LocalReference>? pendingReferences,
            bool destroyAddressSpace,
            bool removeDestroyedExternalReferences,
            CancellationToken ct)
        {
            if (!cleanup.NotificationsFinalized)
            {
                if (host is not null)
                {
                    await host
                        .FinalizeRetiredGenerationNotificationsAsync(nodeManager, ct)
                        .ConfigureAwait(false);
                }
                cleanup.NotificationsFinalized = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.ReferencesRemoved)
            {
                if (pendingReferences is { Count: > 0 })
                {
                    await server.NodeManager
                        .RemoveReferencesAsync(pendingReferences, ct)
                        .ConfigureAwait(false);
                    pendingReferences.Clear();
                }
                cleanup.ReferencesRemoved = true;
                RecordShutdownCleanupProgress();
            }

            if (destroyAddressSpace && !cleanup.Destroyed)
            {
                if (host is not null)
                {
                    await host
                        .DestroyAddressSpaceAsync(
                            nodeManager,
                            ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    await nodeManager.DeleteAddressSpaceAsync(ct).ConfigureAwait(false);
                }
                cleanup.Destroyed = true;
                RecordShutdownCleanupProgress();
            }

            if (removeDestroyedExternalReferences &&
                !cleanup.DestroyedExternalReferencesRemoved)
            {
                if (host is not null)
                {
                    await host
                        .RemoveDestroyedExternalReferencesAsync(nodeManager, ct)
                        .ConfigureAwait(false);
                }
                cleanup.DestroyedExternalReferencesRemoved = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.Released)
            {
                host?.Release(nodeManager);
                cleanup.Released = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.Disposed)
            {
                await DisposeNodeManagerAsync(nodeManager).ConfigureAwait(false);
                cleanup.Disposed = true;
                RecordShutdownCleanupProgress();
            }
        }

        private void RecordShutdownCleanupProgress()
        {
            Interlocked.Increment(ref m_shutdownCleanupProgress);
        }

        private void RemoveRetiredNodeManagerRecord(RetiredNodeManager retired)
        {
            bool removed;
            lock (m_registrationLock)
            {
                removed = m_retiredNodeManagers.Remove(retired);
            }
            if (removed)
            {
                RecordShutdownCleanupProgress();
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

        private async ValueTask<Exception?> CleanupPreparedAsync(
            IServerInternal? server,
            IDynamicNodeManagerHost host,
            PreparedNodeManager prepared,
            bool allowRequestCallback)
        {
            var failures = new List<Exception>();
            if (server is not null && prepared.Published)
            {
                try
                {
                    await FinalizeNotificationsOutsideLifecycleSemaphoreAsync(
                            server,
                            host,
                            prepared.NodeManager,
                            allowShuttingDown: true)
                        .ConfigureAwait(false);
                    await DrainRequestsOutsideLifecycleSemaphoreAsync(
                            server,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    EnsureSameRunningServer(
                        server,
                        host,
                        allowRequestCallback);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failures.Add(ex);
                }
            }

            if (server is not null)
            {
                try
                {
                    await UnbindFromServerAsync(
                        server,
                        prepared.NodeManager,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failures.Add(ex);
                }
            }

            try
            {
                await host
                    .RollbackAsync(prepared, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures.Add(ex);
            }

            if (prepared.Published && server is not null)
            {
                host.SetRetiredGenerationNotifications(
                    prepared.NodeManager,
                    enabled: true);
            }

            if (failures.Count > 1)
            {
                return new AggregateException(
                    "NodeManager finalization, unbinding, or structural rollback failed.",
                    failures);
            }
            return failures.Count == 1 ? failures[0] : null;
        }

        /// <summary>
        /// Schedules a background pass that disposes any shadow-retired generation whose
        /// monitored items and Browse continuations have drained. Invoked from an
        /// ownership-sensitive request or continuation disposal, so
        /// the teardown must never run inline on the request path: it is dispatched to the
        /// thread pool with the request's execution context suppressed. Background and direct
        /// cleanup use the same claim protocol: briefly take the lifecycle semaphore to suspend
        /// retired-generation notifications and invalidate immediate continuation points, release it for
        /// the request drain, then reacquire and revalidate before destruction. This prevents
        /// either cleanup schedule from forming a circular wait with a callback-safe lifecycle
        /// call.
        /// </summary>
        private void ScheduleRetiredGenerationDrainCleanup()
        {
            if (!TryEnterBackgroundOperation())
            {
                return;
            }
            bool scheduled = false;
            lock (m_registrationLock)
            {
                if (m_retiredNodeManagers.Count == 0)
                {
                    ExitLifecycleOperation();
                    return;
                }
            }

            // Suppress the triggering request's execution context so the background pass is
            // not observed as running inside an OPC UA request callback.
            bool restoreFlow = false;
            try
            {
                if (!ExecutionContext.IsFlowSuppressed())
                {
                    ExecutionContext.SuppressFlow();
                    restoreFlow = true;
                }
                // Run refuses the work once the scope shuts down; the operation count
                // must then be released here because the drain never runs.
                scheduled = m_backgroundWork.Run(
                    nameof(DrainRetiredGenerationsAsync),
                    async _ => await DrainRetiredGenerationsAsync().ConfigureAwait(false));
            }
            finally
            {
                if (restoreFlow)
                {
                    ExecutionContext.RestoreFlow();
                }
                if (!scheduled)
                {
                    ExitLifecycleOperation();
                }
            }
        }

        private async Task DrainRetiredGenerationsAsync()
        {
            bool semaphoreHeld = false;
            try
            {
                if (!TryGetRunningServer(
                    out IServerInternal server,
                    out IDynamicNodeManagerHost host))
                {
                    return;
                }

                await m_lifecycleSemaphore.WaitAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                semaphoreHeld = true;
                if (!TryGetRunningServer(
                    out IServerInternal currentServer,
                    out IDynamicNodeManagerHost currentHost) ||
                    !ReferenceEquals(currentServer, server) ||
                    !ReferenceEquals(currentHost, host))
                {
                    return;
                }
                await CleanupRetiredNodeManagersAsync(server, host)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Prompt cleanup is best-effort. Any generation that could not be torn down
                // here is retried by the next lifecycle operation or server shutdown.
            }
            finally
            {
                if (semaphoreHeld)
                {
                    m_lifecycleSemaphore.Release();
                }
                ExitLifecycleOperation();
            }
        }

        private bool TryGetRunningServer(
            out IServerInternal server,
            out IDynamicNodeManagerHost host,
            bool allowShuttingDown = false)
        {
            server = null!;
            host = null!;
            if ((m_disposed || m_shuttingDown) && !allowShuttingDown)
            {
                return false;
            }
            if (!allowShuttingDown &&
                m_server.CurrentState is not (ServerState.Running or ServerState.NoConfiguration))
            {
                return false;
            }

            IServerInternal runningServer = m_server.CurrentInstance;
            if (runningServer.NodeManager is not IDynamicNodeManagerHost dynamicHost)
            {
                return false;
            }

            server = runningServer;
            host = dynamicHost;
            return true;
        }

        private async ValueTask CleanupRetiredNodeManagersAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            bool allowShuttingDown = false)
        {
            while (true)
            {
                bool pendingDrain;
                lock (m_registrationLock)
                {
                    pendingDrain = m_retiredNodeManagers.Any(retired =>
                        retired.DrainPending);
                }

                if (pendingDrain)
                {
                    return;
                }

                RetiredNodeManager[] retired;
                lock (m_registrationLock)
                {
                    retired = [.. m_retiredNodeManagers];
                }

                var claimed = new List<RetiredNodeManager>();
                foreach (RetiredNodeManager retiredNodeManager in retired)
                {
                    if (retiredNodeManager.DrainPending ||
                        (retiredNodeManager.AllowActiveMonitoredItems &&
                            HasRetainedUses(
                                server,
                                retiredNodeManager.NodeManager)))
                    {
                        continue;
                    }

                    if (retiredNodeManager.AllowActiveMonitoredItems &&
                        !retiredNodeManager.NotificationsSuspended)
                    {
                        host.SetRetiredGenerationNotifications(
                            retiredNodeManager.NodeManager,
                            enabled: false);
                        retiredNodeManager.NotificationsSuspended = true;
                    }
                    retiredNodeManager.RequestsDrained = false;
                    retiredNodeManager.DrainPending = true;
                    claimed.Add(retiredNodeManager);
                }

                if (claimed.Count == 0)
                {
                    return;
                }

                try
                {
                    var drainReady = new List<RetiredNodeManager>();
                    foreach (RetiredNodeManager retiredNodeManager in claimed)
                    {
                        await WaitForNotificationDispatchesOutsideLifecycleSemaphoreAsync(
                                server,
                                host,
                                retiredNodeManager.NodeManager,
                                allowShuttingDown)
                            .ConfigureAwait(false);

                        bool stillRetired;
                        lock (m_registrationLock)
                        {
                            stillRetired =
                                m_retiredNodeManagers.Contains(retiredNodeManager);
                        }
                        if (!stillRetired)
                        {
                            continue;
                        }
                        if (retiredNodeManager.AllowActiveMonitoredItems &&
                            HasRetainedUses(
                                server,
                                retiredNodeManager.NodeManager))
                        {
                            host.SetRetiredGenerationNotifications(
                                retiredNodeManager.NodeManager,
                                enabled: true);
                            retiredNodeManager.NotificationsSuspended = false;
                            continue;
                        }
                        if (retiredNodeManager.NeedsDetachment && !retiredNodeManager.AllowActiveMonitoredItems)
                        {
                            InvalidateContinuationPoints(
                                server,
                                retiredNodeManager.NodeManager);
                        }
                        drainReady.Add(retiredNodeManager);
                    }

                    if (drainReady.Count == 0)
                    {
                        return;
                    }

                    m_lifecycleSemaphore.Release();
                    try
                    {
                        await server.RequestManager
                            .WaitForCurrentRequestsAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        await m_lifecycleSemaphore
                            .WaitAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                    }

                    foreach (RetiredNodeManager retiredNodeManager in drainReady)
                    {
                        retiredNodeManager.RequestsDrained = true;
                    }

                    if (!TryGetRunningServer(
                        out IServerInternal currentServer,
                        out IDynamicNodeManagerHost currentHost,
                        allowShuttingDown) ||
                        !ReferenceEquals(currentServer, server) ||
                        !ReferenceEquals(currentHost, host))
                    {
                        return;
                    }

                    foreach (RetiredNodeManager retiredNodeManager in drainReady)
                    {
                        bool stillRetired;
                        lock (m_registrationLock)
                        {
                            stillRetired =
                                m_retiredNodeManagers.Contains(retiredNodeManager);
                        }
                        if (!stillRetired)
                        {
                            continue;
                        }
                        if (retiredNodeManager.AllowActiveMonitoredItems &&
                            HasRetainedUses(
                                server,
                                retiredNodeManager.NodeManager))
                        {
                            host.SetRetiredGenerationNotifications(
                                retiredNodeManager.NodeManager,
                                enabled: true);
                            retiredNodeManager.NotificationsSuspended = false;
                            retiredNodeManager.RequestsDrained = false;
                            continue;
                        }

                        bool cleaned = await CleanupRetiredNodeManagerAsync(
                            server,
                            host,
                            retiredNodeManager,
                            allowShuttingDown).ConfigureAwait(false);
                        if (cleaned)
                        {
                            RemoveRetiredNodeManagerRecord(retiredNodeManager);
                        }
                    }
                }
                finally
                {
                    foreach (RetiredNodeManager retiredNodeManager in claimed)
                    {
                        try
                        {
                            RestoreRetiredNotificationsForActiveItems(
                                server,
                                host,
                                retiredNodeManager);
                        }
                        finally
                        {
                            retiredNodeManager.DrainPending = false;
                        }
                    }
                }
            }
        }

        private void RestoreRetiredNotificationsForActiveItems(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            RetiredNodeManager retired)
        {
            if (!retired.AllowActiveMonitoredItems ||
                !retired.NotificationsSuspended ||
                m_shuttingDown ||
                m_disposed)
            {
                return;
            }

            if (!TryGetRunningServer(
                    out IServerInternal currentServer,
                    out IDynamicNodeManagerHost currentHost) ||
                !ReferenceEquals(currentServer, server) ||
                !ReferenceEquals(currentHost, host))
            {
                return;
            }

            lock (m_registrationLock)
            {
                if (!m_retiredNodeManagers.Contains(retired))
                {
                    return;
                }
            }

            if (!HasRetainedUses(server, retired.NodeManager))
            {
                return;
            }

            host.SetRetiredGenerationNotifications(
                retired.NodeManager,
                enabled: true);
            retired.NotificationsSuspended = false;
            retired.RequestsDrained = false;
        }

        /// <summary>
        /// Detaches and destroys a retired NodeManager generation, returning <c>true</c>
        /// once fully cleaned up. A shadow-reloaded generation that still owns active
        /// monitored items or Browse continuations is left untouched (requests, continuation points, and
        /// monitored items that already captured it keep working) and <c>false</c> is
        /// returned so the caller retries cleanup on a later opportunity. An immediate
        /// retirement instead invalidates owned monitored items before detachment; neither
        /// policy deletes the client's subscription.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The retired generation still has undrained requests or active monitored items when detachment is attempted.
        /// </exception>
        private async ValueTask<bool> CleanupRetiredNodeManagerAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            RetiredNodeManager retired,
            bool allowShuttingDown = false)
        {
            ShutdownCleanupState cleanup = retired.ShutdownCleanup;
            if (retired.NeedsDetachment)
            {
                if (retired.AllowActiveMonitoredItems &&
                    HasRetainedUses(server, retired.NodeManager))
                {
                    return false;
                }

                if (retired.AllowActiveMonitoredItems &&
                    !retired.NotificationsSuspended)
                {
                    host.SetRetiredGenerationNotifications(
                        retired.NodeManager,
                        enabled: false);
                    retired.NotificationsSuspended = true;
                }

                if (!retired.RequestsDrained)
                {
                    throw new InvalidOperationException(
                        "A retired NodeManager cannot be detached before its requests drain.");
                }
                InvalidateContinuationPoints(server, retired.NodeManager);

                // A migrating reload moves its items at commit, but a request routed before
                // the commit can still create one on the retired generation afterwards. If
                // it could not be migrated it is invalidated instead of blocking cleanup.
                if (retired.DetachActiveMonitoredItems ||
                    (!retired.AllowActiveMonitoredItems &&
                        HasActiveMonitoredItems(server, retired.NodeManager)))
                {
                    await DetachActiveMonitoredItemsAsync(
                            server,
                            retired.NodeManager)
                        .ConfigureAwait(false);
                }
                EnsureNoActiveMonitoredItems(server, retired.NodeManager);
                if (retired.NotificationsSuspended &&
                    !cleanup.NotificationsFinalized)
                {
                    await FinalizeNotificationsOutsideLifecycleSemaphoreAsync(
                            server,
                            host,
                            retired.NodeManager,
                            allowShuttingDown)
                        .ConfigureAwait(false);
                    cleanup.NotificationsFinalized = true;
                    RecordShutdownCleanupProgress();
                }
                await UnbindFromServerAsync(
                    server,
                    retired.NodeManager,
                    CancellationToken.None,
                    unsubscribeAllEvents: !retired.AllowActiveMonitoredItems && !cleanup.NotificationsFinalized)
                    .ConfigureAwait(false);
                retired.NeedsDetachment = false;
                if (!cleanup.Detached)
                {
                    cleanup.Detached = true;
                    RecordShutdownCleanupProgress();
                }
            }
            else if (!cleanup.Detached)
            {
                cleanup.Detached = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.ReferencesRemoved)
            {
                if (retired.PendingReferences.Count > 0)
                {
                    await server.NodeManager
                        .RemoveReferencesAsync(
                            retired.PendingReferences,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                    retired.PendingReferences.Clear();
                }
                cleanup.ReferencesRemoved = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.Destroyed)
            {
                await DestroyAddressSpaceOutsideLifecycleSemaphoreAsync(
                        host,
                        retired.NodeManager,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!cleanup.NotificationsFinalized)
                {
                    cleanup.NotificationsFinalized = true;
                    RecordShutdownCleanupProgress();
                }
                cleanup.Destroyed = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.Disposed)
            {
                RebuildActiveTypeTree(server);
                await DisposeNodeManagerAsync(retired.NodeManager).ConfigureAwait(false);
                cleanup.Disposed = true;
                RecordShutdownCleanupProgress();
            }
            return cleanup.Disposed;
        }

        private delegate ValueTask<IAsyncNodeManager> CreateNodeManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken ct);

        /// <summary>
        /// Tracks monitored items while a NodeManager reload detaches them from the retiring
        /// NodeManager and attaches compatible items to the replacement.
        /// </summary>
        internal sealed class MonitoredItemTransition
        {
            /// <summary>
            /// Creates a monitored item transition with the item sets selected during reload
            /// preparation.
            /// </summary>
            /// <param name="server">The server that owns the subscriptions being inspected.</param>
            /// <param name="current">The lifecycle endpoint for the retiring NodeManager.</param>
            /// <param name="replacement">The lifecycle endpoint for the replacement NodeManager, if any.</param>
            /// <param name="compatibleItems">Items that can be handed to the replacement NodeManager.</param>
            /// <param name="deletedItems">Items whose nodes disappeared during reload.</param>
            /// <param name="isOwnedBySubscription">Optional test hook that verifies subscription ownership.</param>
            public MonitoredItemTransition(
                IServerInternal server,
                INodeManagerMonitoredItemLifecycle current,
                INodeManagerMonitoredItemLifecycle? replacement,
                IReadOnlyList<IMonitoredItem> compatibleItems,
                IReadOnlyList<IMonitoredItem> deletedItems,
                Func<IMonitoredItem, bool>? isOwnedBySubscription = null)
            {
                m_server = server;
                m_current = current;
                m_replacement = replacement;
                m_compatibleItems = compatibleItems;
                m_deletedItems = deletedItems;
                m_isOwnedBySubscription = isOwnedBySubscription;
            }

            /// <summary>
            /// Detaches every item still owned by a subscription from the retiring NodeManager.
            /// </summary>
            /// <param name="ct">The token that aborts detach work.</param>
            /// <returns>A task that completes when all current items have detached.</returns>
            /// <exception cref="ServiceResultException">The retiring NodeManager rejects a detach.</exception>
            public async ValueTask DetachCurrentAsync(CancellationToken ct)
            {
                foreach (IMonitoredItem monitoredItem in m_compatibleItems.Concat(m_deletedItems))
                {
                    // A Subscription being deleted, a Session being closed or a Client deleting the
                    // item can remove it while the transition runs. There is then nothing to move,
                    // so the item is skipped instead of failing the lifecycle operation.
                    if (!IsOwnedBySubscription(monitoredItem))
                    {
                        continue;
                    }

                    ServiceResult result = await m_current.DetachMonitoredItemAsync(
                        monitoredItem,
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
                    m_detachedItems.Add(monitoredItem);
                }
            }

            /// <summary>
            /// Attaches compatible items to the replacement NodeManager and records failures
            /// that should delete the item instead.
            /// </summary>
            /// <param name="ct">The token that aborts attach work.</param>
            /// <returns>The non-fatal failures that occurred while attempting the attach.</returns>
            public async ValueTask<List<Exception>> AttachCompatibleAsync(
                CancellationToken ct)
            {
                var failures = new List<Exception>();
                if (m_replacement is null)
                {
                    return failures;
                }

                foreach (IMonitoredItem monitoredItem in m_compatibleItems)
                {
                    if (!IsOwnedBySubscription(monitoredItem))
                    {
                        continue;
                    }

                    // Every MonitoredItem the server creates is detachable. A test double or
                    // a foreign implementation that is not cannot take part in the reservation, so
                    // it is handed over without one.
                    var detachable = monitoredItem as IDetachableMonitoredItem;
                    if (detachable?.TryBeginAttach() == false)
                    {
                        // The item was deleted and disposed before the hand-over started.
                        continue;
                    }

                    bool attached = false;
                    try
                    {
                        ServiceResult result = await m_replacement.AttachMonitoredItemAsync(
                            monitoredItem,
                            ct)
                            .ConfigureAwait(false);
                        if (ServiceResult.IsGood(result))
                        {
                            attached = true;
                            m_attachedItems.Add(monitoredItem);
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
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        MarkAttachFailure(monitoredItem);
                        failures.Add(ex);
                    }

                    if (detachable is null || detachable.EndAttach() || !attached)
                    {
                        continue;
                    }

                    // The item was deleted and disposed while it was being handed over, so the
                    // replacement must not be left sampling it.
                    m_attachedItems.Remove(monitoredItem);
                    try
                    {
                        await m_replacement.DetachMonitoredItemAsync(
                            monitoredItem,
                            ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        failures.Add(ex);
                    }

                    detachable.Detach(m_server);
                }

                return failures;
            }

            /// <summary>
            /// Marks deleted or incompatible items as removed after the replacement transition
            /// succeeds.
            /// </summary>
            public void MarkDeletedItems()
            {
                foreach (IMonitoredItem monitoredItem in m_deletedItems.Concat(m_failedItems))
                {
                    if (IsOwnedBySubscription(monitoredItem))
                    {
                        var lifecycle = (IDetachableMonitoredItem)monitoredItem;
                        lifecycle.Detach(m_server);
                        lifecycle.MarkNodeDeleted();
                    }
                }
            }

            /// <summary>
            /// Reverses the detach and attach steps already completed by this transition.
            /// </summary>
            /// <param name="ct">The token that aborts rollback work.</param>
            /// <returns>A task that completes when the transition has been restored.</returns>
            /// <exception cref="AggregateException">One or more monitored items could not be restored.</exception>
            public async ValueTask RollbackAsync(CancellationToken ct)
            {
                var failures = new List<Exception>();
                if (m_replacement is not null)
                {
                    for (int ii = m_attachedItems.Count - 1; ii >= 0; ii--)
                    {
                        try
                        {
                            ServiceResult result = await m_replacement.DetachMonitoredItemAsync(
                                m_attachedItems[ii],
                                ct)
                                .ConfigureAwait(false);
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

                for (int ii = m_detachedItems.Count - 1; ii >= 0; ii--)
                {
                    if (!IsOwnedBySubscription(m_detachedItems[ii]))
                    {
                        continue;
                    }

                    try
                    {
                        ServiceResult result = await m_current.RecoverMonitoredItemAsync(
                            m_detachedItems[ii],
                            ct)
                            .ConfigureAwait(false);
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

                if (failures.Count > 0)
                {
                    throw new AggregateException(
                        "One or more monitored items could not be restored.",
                        failures);
                }
                m_attachedItems.Clear();
                m_detachedItems.Clear();
            }

            private void MarkAttachFailure(IMonitoredItem monitoredItem)
            {
                var lifecycle = (IDetachableMonitoredItem)monitoredItem;
                lifecycle.Detach(m_server);
                lifecycle.MarkNodeDeleted();
                m_failedItems.Add(monitoredItem);
            }

            private bool IsOwnedBySubscription(IMonitoredItem monitoredItem)
            {
                if (m_isOwnedBySubscription is not null)
                {
                    return m_isOwnedBySubscription(monitoredItem);
                }

                foreach (ISubscription subscription in m_server.SubscriptionManager.GetSubscriptions())
                {
                    if (subscription is ISubscriptionMonitoredItemLifecycle lifecycle &&
                        lifecycle.ContainsMonitoredItem(monitoredItem))
                    {
                        return true;
                    }
                }
                return false;
            }

            private readonly IServerInternal m_server;
            private readonly INodeManagerMonitoredItemLifecycle m_current;
            private readonly INodeManagerMonitoredItemLifecycle? m_replacement;
            private readonly IReadOnlyList<IMonitoredItem> m_compatibleItems;
            private readonly IReadOnlyList<IMonitoredItem> m_deletedItems;
            private readonly Func<IMonitoredItem, bool>? m_isOwnedBySubscription;
            private readonly List<IMonitoredItem> m_detachedItems = [];
            private readonly List<IMonitoredItem> m_attachedItems = [];
            private readonly List<IMonitoredItem> m_failedItems = [];
        }

        private sealed class UnsupportedMonitoredItemLifecycle :
            INodeManagerMonitoredItemLifecycle
        {
            public static UnsupportedMonitoredItemLifecycle Instance { get; } = new();

            public ValueTask<IReadOnlyList<IMonitoredItem>> GetMonitoredItemsSnapshotAsync(
                IReadOnlyCollection<NodeId>? nodeIds = null,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IReadOnlyList<IMonitoredItem>>([]);
            }

            public ValueTask<ServiceResult> CanAttachMonitoredItemAsync(
                IMonitoredItem monitoredItem,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(
                    new ServiceResult(StatusCodes.BadNotSupported));
            }

            public ValueTask<ServiceResult> DetachMonitoredItemAsync(
                IMonitoredItem monitoredItem,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(
                    new ServiceResult(StatusCodes.BadNotSupported));
            }

            public ValueTask<ServiceResult> AttachMonitoredItemAsync(
                IMonitoredItem monitoredItem,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(
                    new ServiceResult(StatusCodes.BadNotSupported));
            }

            public ValueTask<ServiceResult> RecoverMonitoredItemAsync(
                IMonitoredItem monitoredItem,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<ServiceResult>(
                    new ServiceResult(StatusCodes.BadNotSupported));
            }
        }

        private enum ReloadRetirementMode
        {
            Migrate,
            Graceful,
            Immediate
        }

        private sealed class RegistrationState
        {
            public RegistrationState(
                NodeManagerRegistration registration,
                PreparedNodeManager prepared,
                bool allowLifecycleFromRequestCallback)
            {
                Registration = registration;
                Prepared = prepared;
                AllowLifecycleFromRequestCallback = allowLifecycleFromRequestCallback;
            }

            public NodeManagerRegistration Registration { get; }

            public PreparedNodeManager Prepared { get; }

            public bool AllowLifecycleFromRequestCallback { get; }

            public bool RemovalPending { get; set; }

            /// <summary>
            /// Gets or sets whether the readiness callback of the registration has not yet completed.
            /// </summary>
            public bool ReadinessPending { get; set; }

            public ShutdownCleanupState ShutdownCleanup { get; } = new();
        }

        private sealed class ServerBindings
        {
            public Dictionary<NodeId, SessionBinding> Sessions { get; } = [];

            public Dictionary<uint, IEventMonitoredItem> EventMonitoredItems { get; } = [];
        }

        private sealed class SessionBinding
        {
            public SessionBinding(
                ISession session,
                IUserIdentity identity)
            {
                Session = session;
                Identity = identity;
            }

            public ISession Session { get; }

            public IUserIdentity Identity { get; }
        }

        private sealed class RetiredNodeManager
        {
            public RetiredNodeManager(
                IAsyncNodeManager nodeManager,
                List<LocalReference> pendingReferences,
                bool needsDetachment,
                bool allowActiveMonitoredItems = false,
                bool detachActiveMonitoredItems = false)
            {
                NodeManager = nodeManager;
                PendingReferences = pendingReferences;
                NeedsDetachment = needsDetachment;
                AllowActiveMonitoredItems = allowActiveMonitoredItems;
                DetachActiveMonitoredItems = detachActiveMonitoredItems;
            }

            public IAsyncNodeManager NodeManager { get; }

            public List<LocalReference> PendingReferences { get; }

            public bool NeedsDetachment { get; set; }

            /// <summary>
            /// Gets or sets whether session and existing all-events notifications have
            /// been suspended before the final request drain.
            /// </summary>
            public bool NotificationsSuspended { get; set; }

            /// <summary>
            /// Gets or sets whether all requests that could still call this generation
            /// have drained since notifications and continuation points were cut off.
            /// </summary>
            public bool RequestsDrained { get; set; }

            /// <summary>
            /// Gets or sets whether a background or direct two-phase drain currently owns
            /// cleanup. Callback-safe lifecycle operations skip the generation until that
            /// pass revalidates.
            /// </summary>
            public bool DrainPending { get; set; }

            /// <summary>
            /// Gets whether this generation was retired by a shadow reload and may still
            /// own active monitored items. Cleanup is deferred rather than rejected while
            /// this holds true and monitored items remain.
            /// </summary>
            public bool AllowActiveMonitoredItems { get; }

            /// <summary>
            /// Gets whether active monitored items should be detached and marked deleted
            /// before destruction. Prepared batches cut off these sources before readiness;
            /// destruction still waits for requests using this generation to drain.
            /// </summary>
            public bool DetachActiveMonitoredItems { get; }

            public ShutdownCleanupState ShutdownCleanup { get; } = new();
        }

        private sealed class ShutdownCleanupState
        {
            public bool RemovalUnpublished { get; set; }

            public bool RemovalMonitoredItemsDeleted { get; set; }

            public bool Detached { get; set; }

            public bool NotificationsFinalized { get; set; }

            public bool ReferencesRemoved { get; set; }

            public bool Destroyed { get; set; }

            public bool DestroyedExternalReferencesRemoved { get; set; }

            public bool Released { get; set; }

            public bool Disposed { get; set; }
        }

        private sealed class OperationLifetime : IDisposable
        {
            /// <summary>
            /// Creates a lifecycle operation scope.
            /// </summary>
            /// <param name="owner">The lifecycle that owns the operation.</param>
            /// <param name="parent">The operation active when this one started.</param>
            public OperationLifetime(NodeManagerLifecycle owner, OperationLifetime? parent)
            {
                m_owner = owner;
                Parent = parent;
            }

            /// <summary>
            /// Gets the operation that was active when this one started.
            /// </summary>
            public OperationLifetime? Parent { get; }

            /// <summary>
            /// Determines whether the operation belongs to the specified lifecycle and has not ended.
            /// </summary>
            /// <param name="owner">The lifecycle to compare with.</param>
            /// <returns>True if the operation is still owned by the lifecycle.</returns>
            public bool IsOwnedBy(NodeManagerLifecycle owner)
            {
                return ReferenceEquals(Volatile.Read(ref m_owner), owner);
            }

            public void Dispose()
            {
                NodeManagerLifecycle? owner = Interlocked.Exchange(ref m_owner, null);
                if (owner is not null)
                {
                    if (ReferenceEquals(owner.m_currentOperation.Value, this))
                    {
                        owner.m_currentOperation.Value = Parent;
                    }
                    owner.ExitLifecycleOperation();
                }
            }

            private NodeManagerLifecycle? m_owner;
        }

        /// <inheritdoc/>
        public ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            IOperationContext? callerContext,
            CancellationToken cancellationToken = default)
        {
            return PrepareBatchAsync(changes, null, callerContext, cancellationToken);
        }

        /// <summary>
        /// Validates a publication unit and prepares its node managers privately, without publishing them.
        /// </summary>
        /// <param name="changes">The additions, replacements and retirements of the unit.</param>
        /// <param name="publication">The invocation that owns publication admission, or null for a standalone
        /// batch.</param>
        /// <param name="callerContext">The operation the caller is running under, or null for an ordinary external
        /// caller. It is retained by the prepared batch for its commit.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <param name="readImages">Read images to publish together with the changes.</param>
        /// <param name="validationOnly">Whether the batch is a validation candidate that can never be
        /// committed.</param>
        /// <returns>The prepared batch, which must be disposed if it is not committed.</returns>
        private async ValueTask<IPreparedNodeManagerBatch> PrepareBatchAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            PublicationInvocation? publication,
            IOperationContext? callerContext,
            CancellationToken cancellationToken,
            ArrayOf<INodeManagerReadImage> readImages = default,
            bool validationOnly = false)
        {
            if (changes.IsNull || (changes.Count == 0 && readImages.Count == 0))
            {
                throw new ArgumentException("A publication unit must contain at least one change.", nameof(changes));
            }
            changes = [.. changes];
            readImages = [.. readImages];
            var identities = new HashSet<Guid>();
            bool allowRequestCallback = true;
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
                    allowRequestCallback &= GetCurrentState(current).AllowLifecycleFromRequestCallback;
                }
                if (change.Factory is { } factory)
                {
                    ValidateFactoryRegistration(factory, change.Current);
                    allowRequestCallback &= IsRequestCallbackSafe(factory);
                }
            }

            var entries = new List<BatchEntry>();
            OperationLifetime? operation = null;
            try
            {
                operation = EnterLifecycleOperation(publication?.Capture);
                (IServerInternal server, IDynamicNodeManagerHost host) =
                    GetRunningServer(allowRequestCallback, callerContext);
                if (host is not IDynamicNodeManagerBatchHost batchHost)
                {
                    throw new NotSupportedException("The server host does not support prepared publication.");
                }
                if (server.Factory is not EncodeableFactory servingFactory)
                {
                    throw new NotSupportedException("The server factory does not support prepared publication.");
                }
                using IDisposable routing = batchHost.UseLiveRouting();
                using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? waiter =
                    EnterRequestLifecycleWaiter(server, callerContext);
                await using var bindingAdmission =
                    batchHost.SuspendBindingAdmission(callerContext).ConfigureAwait(false);
                await WaitForLifecycleSemaphoreAsync(waiter, cancellationToken).ConfigureAwait(false);
                TypeTable? preparedTypes = null;
                EncodeableFactory? preparedFactory = null;
                try
                {
                    EnsureSameRunningServer(server, host, allowRequestCallback);
                    preparedTypes = server.TypeTree.CaptureSnapshot(out TypeTable originalTypes, out long typeRevision);
                    preparedFactory = servingFactory.CaptureSnapshot(
                        out EncodeableFactory originalFactory, out long factoryRevision);
                    using IDisposable types = batchHost.UseTypeImage(preparedTypes, preparedFactory);
                    IDataTypeDefinitionResolver? resolver = null;
                    int namespaceCount = server.NamespaceUris.Count;
                    for (int index = 0; index < changes.Count; index++)
                    {
                        NodeManagerBatchChange change = changes[index];
                        cancellationToken.ThrowIfCancellationRequested();
                        RegistrationState? current = change.Current is null
                            ? null
                            : GetCurrentState(change.Current);
                        var entry = new BatchEntry(change, current);
                        entries.Add(entry);
                        if (change.Factory is null)
                        {
                            continue;
                        }
                        entry.Manager = await change.Factory.CreateAsync(
                            server, m_server.CurrentConfiguration, cancellationToken).ConfigureAwait(false) ??
                            throw new InvalidOperationException("A batch factory returned no NodeManager.");
                        entry.Prepared = await host.PrepareAsync(entry.Manager, cancellationToken)
                            .ConfigureAwait(false);
                        await ValidateDataTypeCompatibilityAsync(server, entry.Manager, cancellationToken)
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
                            await host.ReplaceAsync(
                                current.Prepared.NodeManager, entry.Prepared,
                                allowActiveMonitoredItems: true,
                                retainReplacedNotifications: !change.Immediate,
                                ct: cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await host.PublishAsync(entry.Prepared, cancellationToken).ConfigureAwait(false);
                        }
                        resolver = await m_server.RefreshComplexTypesAsync(
                            server, entry.Manager, publishResolver: false, cancellationToken: cancellationToken)
                            .ConfigureAwait(false) ??
                            resolver;
                        var registration = new NodeManagerRegistration(
                            current?.Registration.Id ?? Guid.NewGuid(),
                            (current?.Registration.Generation ?? 0) + 1,
                            entry.Manager);
                        entry.Next = new RegistrationState(registration, entry.Prepared, allowRequestCallback)
                        {
                            ReadinessPending = entry.Manager is INodeManagerReadinessNotification
                        };
                    }
                    // Staging can register hidden namespace routes; freeze its completed routing image.
                    var prepared = new PreparedBatch(
                        this, server, host, entries, operation, namespaceCount, allowRequestCallback,
                        batchHost.RoutingRevision, preparedTypes, originalTypes, typeRevision,
                        preparedFactory, originalFactory, factoryRevision, resolver, validationOnly, callerContext);
                    if (readImages.Count != 0)
                    {
                        prepared.BindReadImages(readImages);
                    }
                    operation = null;
                    return prepared;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    using IDisposable? types = preparedTypes is null || preparedFactory is null
                        ? null
                        : batchHost.UseTypeImage(preparedTypes, preparedFactory);
                    Exception? cleanup = await AbortBatchEntriesAsync(host, entries, allowRequestCallback)
                        .ConfigureAwait(false);
                    if (cleanup is not null)
                    {
                        throw new AggregateException("Batch preparation and abort both failed.", failure, cleanup);
                    }
                    throw;
                }
                finally
                {
                    m_lifecycleSemaphore.Release();
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
            using IDisposable routing = ((IDynamicNodeManagerBatchHost)batch.Host).UseLiveRouting();
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? waiter =
                EnterRequestLifecycleWaiter(batch.Server);
            await using var bindingAdmission =
                ((IDynamicNodeManagerBatchHost)batch.Host).SuspendBindingAdmission(batch.CallerContext)
                    .ConfigureAwait(false);
            await WaitForLifecycleSemaphoreAsync(waiter, cancellationToken).ConfigureAwait(false);
            var retired = new List<RetiredNodeManager>();
            var failures = new List<Exception>();
            try
            {
                EnsureSameRunningServer(batch.Server, batch.Host, batch.AllowRequestCallback);
                foreach (BatchEntry entry in batch.Entries)
                {
                    if (entry.Current is not null &&
                        !ReferenceEquals(GetCurrentState(entry.Current.Registration), entry.Current))
                    {
                        throw new InvalidOperationException("A registration changed while the batch was prepared.");
                    }
                    if (entry.Current is not null)
                    {
                        retired.Add(new RetiredNodeManager(
                            entry.Current.Prepared.NodeManager,
                            entry.DroppedReferences,
                            needsDetachment: true,
                            allowActiveMonitoredItems: !entry.Change.Immediate,
                            detachActiveMonitoredItems: entry.Change.Immediate)
                        {
                            DrainPending = entry.Change.Immediate
                        });
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

                await ((IDynamicNodeManagerBatchHost)batch.Host).CommitBatchAsync(
                    new NodeManagerBatchContext(
                        Candidates: candidates,
                        Removed: removed,
                        ImmediateRetirements: retired.Where(generation => generation.DetachActiveMonitoredItems)
                            .Select(generation => generation.NodeManager).ToArrayOf(),
                        RoutingRevision: batch.RoutingRevision,
                        TypeTree: batch.TypeTree,
                        OriginalTypes: batch.OriginalTypes,
                        TypeRevision: batch.TypeRevision,
                        Factory: batch.Factory,
                        OriginalFactory: batch.OriginalFactory,
                        FactoryRevision: batch.FactoryRevision,
                        ReadImages: batch.ReadImages,
                        DecideAsync: decideAsync,
                        Published: () =>
                        {
                            batch.IsCommitted = true;
                            if (batch.Resolver is not null)
                            {
                                m_server.ComplexTypeResolverHolder?.SetResolver(batch.Resolver);
                            }
                            lock (m_registrationLock)
                            {
                                foreach (BatchEntry entry in batch.Entries)
                                {
                                    if (entry.Current is not null)
                                    {
                                        entry.Current.Prepared.Published = false;
                                        m_registrations.Remove(entry.Current.Registration.Id);
                                    }
                                    if (entry.Next is not null)
                                    {
                                        m_registrations.Add(entry.Next.Registration.Id, entry.Next);
                                    }
                                }
                                m_retiredNodeManagers.AddRange(retired);
                            }
                            foreach (RetiredNodeManager generation in retired)
                            {
                                if (!generation.DetachActiveMonitoredItems)
                                {
                                    continue;
                                }
                                try
                                {
                                    batch.Host.SetRetiredGenerationNotifications(generation.NodeManager, enabled: false);
                                    generation.NotificationsSuspended = true;
                                }
                                catch (Exception failure) when (failure is not OutOfMemoryException)
                                {
                                    failures.Add(failure);
                                }
                            }
                            try
                            {
                                publishCommittedState?.Invoke();
                            }
                            catch (Exception failure) when (failure is not OutOfMemoryException)
                            {
                                failures.Add(failure);
                            }
                        },
                        ReconcileBindingsAsync: async () =>
                        {
                            await CutOffImmediateBatchSourcesAsync(batch, retired, failures).ConfigureAwait(false);
                            foreach (BatchEntry entry in batch.Entries)
                            {
                                if (entry.Manager is null)
                                {
                                    continue;
                                }
                                try
                                {
                                    await ReconcileBindingsAsync(
                                        batch.Server,
                                        entry.Manager,
                                        new ServerBindings(),
                                        batch.CallerContext,
                                        CancellationToken.None)
                                        .ConfigureAwait(false);
                                }
                                catch (Exception failure) when (failure is not OutOfMemoryException)
                                {
                                    failures.Add(failure);
                                }
                            }
                        },
                        ReportCleanupFailure: failures.Add,
                        CancellationToken: cancellationToken)).ConfigureAwait(false);
                if (decideAsync is null)
                {
                    return null;
                }

                foreach (BatchEntry entry in batch.Entries)
                {
                    if (entry.Manager is null || entry.Next is null)
                    {
                        continue;
                    }
                    try
                    {
                        if (!entry.Change.Immediate)
                        {
                            await RecoverDetachedMonitoredItemsAsync(
                                batch.Server, entry.Manager, CancellationToken.None).ConfigureAwait(false);
                        }
                        await CompleteReadinessOutsideLifecycleSemaphoreAsync(
                            batch.Server, batch.Host, entry.Next, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        failures.Add(failure);
                    }
                    finally
                    {
                        ReleaseReadinessClaim(entry.Next);
                    }
                }
                batch.Host.SetRetiredGenerationDrainObserver(ScheduleRetiredGenerationDrainCleanup);
                try
                {
                    await CleanupRetiredNodeManagersAsync(batch.Server, batch.Host).ConfigureAwait(false);
                    if (batch.Entries.Count != 0)
                    {
                        await NotifyCommittedChangeAsync(
                            batch.Server, "batch", batch.NamespaceCount, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    failures.Add(failure);
                }
                uint completed;
                lock (m_registrationLock)
                {
                    completed = (uint)retired.Count(item => !m_retiredNodeManagers.Contains(item));
                }
                return new NodeManagerBatchResult(
                    batch.Registrations, completed,
                    failures.Count == 0 ? null : new AggregateException(
                        "The batch committed; generation reconciliation remains pending.", failures));
            }
            finally
            {
                m_lifecycleSemaphore.Release();
            }
        }

        /// <summary>
        /// Detaches monitored items and finalizes notifications of retired generations that must be cut off
        /// immediately, collecting failures instead of throwing.
        /// </summary>
        /// <param name="batch">The batch being committed.</param>
        /// <param name="retired">The generations retired by the batch.</param>
        /// <param name="failures">The list that receives failures.</param>
        /// <returns>A task that completes when the sources were cut off.</returns>
        private async ValueTask CutOffImmediateBatchSourcesAsync(
            PreparedBatch batch,
            List<RetiredNodeManager> retired,
            List<Exception> failures)
        {
            try
            {
                foreach (RetiredNodeManager generation in retired)
                {
                    if (!generation.DetachActiveMonitoredItems)
                    {
                        continue;
                    }
                    try
                    {
                        // Cut off sources under publication admission, not the later request/destruction drain.
                        await WaitForNotificationDispatchesOutsideLifecycleSemaphoreAsync(
                            batch.Server, batch.Host, generation.NodeManager).ConfigureAwait(false);
                        await DetachActiveMonitoredItemsAsync(batch.Server, generation.NodeManager)
                            .ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        failures.Add(failure);
                    }
                    try
                    {
                        await FinalizeNotificationsOutsideLifecycleSemaphoreAsync(
                            batch.Server, batch.Host, generation.NodeManager).ConfigureAwait(false);
                        generation.ShutdownCleanup.NotificationsFinalized = true;
                        RecordShutdownCleanupProgress();
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        failures.Add(failure);
                    }
                }
            }
            finally
            {
                foreach (RetiredNodeManager generation in retired)
                {
                    if (generation.DetachActiveMonitoredItems)
                    {
                        generation.DrainPending = false;
                    }
                }
            }
        }

        /// <summary>
        /// Rolls back the entries of a batch that was not published by cleaning up prepared node managers and disposing
        /// created managers, in reverse order.
        /// </summary>
        /// <param name="host">The dynamic node manager host.</param>
        /// <param name="entries">The batch entries to abort.</param>
        /// <param name="allowRequestCallback">Whether the cleanup may run from a request callback.</param>
        /// <returns>The aggregated cleanup failure, or null if the cleanup succeeded.</returns>
        private async ValueTask<Exception?> AbortBatchEntriesAsync(
            IDynamicNodeManagerHost host,
            List<BatchEntry> entries,
            bool allowRequestCallback)
        {
            var failures = new List<Exception>();
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                BatchEntry entry = entries[index];
                if (entry.Prepared is not null)
                {
                    Exception? failure = await CleanupPreparedAsync(
                        null, host, entry.Prepared, allowRequestCallback).ConfigureAwait(false);
                    if (failure is not null)
                    {
                        failures.Add(failure);
                    }
                }
                if (entry.Manager is not null && entry.Prepared?.Published != true)
                {
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
            /// <param name="allowRequestCallback">Whether the batch may be completed from a request callback.</param>
            /// <param name="routingRevision">The routing revision the batch was prepared against.</param>
            /// <param name="typeTree">The private type tree image.</param>
            /// <param name="originalTypes">The published type tree to restore.</param>
            /// <param name="typeRevision">The revision of the published type tree.</param>
            /// <param name="factory">The private encodeable factory image.</param>
            /// <param name="originalFactory">The published encodeable factory.</param>
            /// <param name="factoryRevision">The revision of the published factory.</param>
            /// <param name="resolver">The data type definition resolver, or null.</param>
            /// <param name="validationOnly">Whether the batch is a validation candidate.</param>
            /// <param name="callerContext">The operation the batch was prepared under, or null for an ordinary
            /// external caller.</param>
            public PreparedBatch(
                NodeManagerLifecycle owner,
                IServerInternal server,
                IDynamicNodeManagerHost host,
                List<BatchEntry> entries,
                OperationLifetime operation,
                int namespaceCount,
                bool allowRequestCallback,
                NodeManagerRoutingTable.RoutingSnapshot routingRevision,
                TypeTable typeTree,
                TypeTable originalTypes,
                long typeRevision,
                EncodeableFactory factory,
                EncodeableFactory originalFactory,
                long factoryRevision,
                IDataTypeDefinitionResolver? resolver,
                bool validationOnly,
                IOperationContext? callerContext)
            {
                m_owner = owner;
                CallerContext = callerContext;
                Server = server;
                Host = host;
                Entries = entries;
                m_operation = operation;
                NamespaceCount = namespaceCount;
                AllowRequestCallback = allowRequestCallback;
                RoutingRevision = routingRevision;
                TypeTree = typeTree;
                OriginalTypes = originalTypes;
                TypeRevision = typeRevision;
                Factory = factory;
                OriginalFactory = originalFactory;
                FactoryRevision = factoryRevision;
                Resolver = resolver;
                IsValidationOnly = validationOnly;
                Registrations = entries.Where(entry => entry.Next is not null)
                    .Select(entry => entry.Next!.Registration).ToArrayOf();
            }

            /// <summary>
            /// Gets the operation the batch was prepared under, or null for an ordinary external caller.
            /// </summary>
            public IOperationContext? CallerContext { get; }

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
            /// Gets whether the batch may be completed from a request callback.
            /// </summary>
            public bool AllowRequestCallback { get; }

            /// <summary>
            /// Gets the routing revision the batch was prepared against.
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
                if (operation is null)
                {
                    return;
                }
                try
                {
                    if (!IsCommitted)
                    {
                        using IDisposable routing = ((IDynamicNodeManagerBatchHost)Host).UseLiveRouting();
                        using IDisposable types = ((IDynamicNodeManagerBatchHost)Host).UseTypeImage(TypeTree, Factory);
                        await m_owner.m_lifecycleSemaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                        try
                        {
                            Exception? failure = await m_owner.AbortBatchEntriesAsync(
                                Host, Entries, AllowRequestCallback).ConfigureAwait(false);
                            if (failure is not null)
                            {
                                throw failure;
                            }
                        }
                        finally
                        {
                            m_owner.m_lifecycleSemaphore.Release();
                        }
                    }
                }
                finally
                {
                    operation.Dispose();
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
                    return await m_owner.CompleteBatchAsync(
                        this, decideAsync, cancellationToken, publishCommittedState).ConfigureAwait(false);
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
        public bool SupportsPublicationIsolation => true;

        /// <inheritdoc/>
        public INodeManagerPublicationCapture CapturePublication()
        {
            (IServerInternal server, IDynamicNodeManagerHost host) = GetRunningServer(allowRequestCallback: true);
            if (host is not IDynamicNodeManagerBatchHost batchHost || server.Factory is not EncodeableFactory factory)
            {
                throw new NotSupportedException("The server cannot capture publication revisions.");
            }
            using IDisposable routing = batchHost.UseLiveRouting();
            _ = server.TypeTree.CaptureSnapshot(out TypeTable types, out long typeRevision);
            _ = factory.CaptureSnapshot(out EncodeableFactory originalFactory, out long factoryRevision);
            return new PublicationCapture(
                this, server, host, batchHost, batchHost.RoutingRevision, types, typeRevision,
                originalFactory, factoryRevision);
        }

        /// <summary>
        /// Begins a publication invocation for a capture if no other lifecycle operation owns admission and the
        /// captured revisions are still current.
        /// </summary>
        /// <param name="capture">The capture the invocation belongs to.</param>
        /// <param name="callerContext">The operation the caller is running under, or null for an ordinary external
        /// caller.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>The publication invocation.</returns>
        private PublicationInvocation BeginPublication(
            PublicationCapture capture,
            IOperationContext? callerContext,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_operationLifetimeLock)
            {
                int ancestors = 0;
                for (OperationLifetime? operation = m_currentOperation.Value; operation is not null;
                    operation = operation.Parent)
                {
                    if (operation.IsOwnedBy(this))
                    {
                        ancestors++;
                    }
                }
                if (m_publicationCapture is not null || m_activeLifecycleOperations != ancestors)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadServerTooBusy, "Another lifecycle operation owns publication admission.");
                }
                EnsureSameRunningServer(capture.Server, capture.OwnerHost, allowRequestCallback: true);
                using IDisposable routing = capture.Host.UseLiveRouting();
                _ = ((EncodeableFactory)capture.Server.Factory).CaptureSnapshot(
                    out EncodeableFactory factory, out long factoryRevision);
                bool current = ReferenceEquals(capture.Host.RoutingRevision, capture.RoutingRevision) &&
                    capture.Server.TypeTree.IsCurrentSnapshot(capture.Types, capture.TypeRevision) &&
                    ReferenceEquals(factory, capture.Factory) && factoryRevision == capture.FactoryRevision;
                var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_publicationCapture = capture;
                m_publicationFinished = finished;
                try
                {
                    OperationLifetime operation = EnterLifecycleOperation(capture);
                    return new PublicationInvocation(this, capture, current, finished, operation, callerContext);
                }
                catch
                {
                    m_publicationCapture = null;
                    m_publicationFinished = null;
                    throw;
                }
            }
        }

        /// <summary>
        /// Enters shutdown once no publication invocation owns admission.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task that completes when the active operations have drained.</returns>
        private async ValueTask<Task> EnterShutdownAfterPublicationAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                Task finished;
                lock (m_operationLifetimeLock)
                {
                    if (m_publicationCapture is null)
                    {
                        return EnterShutdownMethod();
                    }
                    finished = m_publicationFinished!.Task;
                }
                await finished.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Immutable record of the routing, type and factory revisions captured for a publication.
        /// </summary>
        /// <param name="Owner">The lifecycle that created the capture.</param>
        /// <param name="Server">The running server.</param>
        /// <param name="OwnerHost">The dynamic node manager host.</param>
        /// <param name="Host">The batch host of the node manager.</param>
        /// <param name="RoutingRevision">The captured routing revision.</param>
        /// <param name="Types">The captured type tree.</param>
        /// <param name="TypeRevision">The revision of the captured type tree.</param>
        /// <param name="Factory">The captured encodeable factory.</param>
        /// <param name="FactoryRevision">The revision of the captured factory.</param>
        private sealed record PublicationCapture(
            NodeManagerLifecycle Owner,
            IServerInternal Server,
            IDynamicNodeManagerHost OwnerHost,
            IDynamicNodeManagerBatchHost Host,
            NodeManagerRoutingTable.RoutingSnapshot RoutingRevision,
            TypeTable Types,
            long TypeRevision,
            EncodeableFactory Factory,
            long FactoryRevision) : INodeManagerPublicationCapture
        {
            /// <inheritdoc/>
            public ValueTask<INodeManagerPublication> BeginAsync(
                IOperationContext? callerContext,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<INodeManagerPublication>(
                    Owner.BeginPublication(this, callerContext, cancellationToken));
            }
        }

        /// <summary>
        /// Admitted publication that prepares and owns batches until it is disposed.
        /// </summary>
        /// <param name="owner">The lifecycle that admitted the publication.</param>
        /// <param name="capture">The capture the publication belongs to.</param>
        /// <param name="current">Whether the captured revisions were still current at admission.</param>
        /// <param name="finished">The completion source set when the publication ends.</param>
        /// <param name="operation">The lifecycle operation held for the publication.</param>
        /// <param name="callerContext">The operation the publication was begun under, or null for an ordinary
        /// external caller.</param>
        private sealed class PublicationInvocation(
            NodeManagerLifecycle owner,
            PublicationCapture capture,
            bool current,
            TaskCompletionSource<bool> finished,
            OperationLifetime operation,
            IOperationContext? callerContext) : INodeManagerValidationPublication
        {
            /// <summary>
            /// Gets whether the captured revisions were current when the publication was admitted.
            /// </summary>
            public bool IsCurrent { get; } = current;

            /// <summary>
            /// Gets the capture the publication belongs to.
            /// </summary>
            public PublicationCapture Capture { get; } = capture;

            /// <summary>
            /// Gets a task that completes when the publication ends.
            /// </summary>
            public Task Finished => m_finished.Task;

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

                using IDisposable namespaces = Capture.Server.NamespaceUris.UsePrivateCopy();
                using IDisposable servers = Capture.Server.ServerUris.UsePrivateCopy();
                IPreparedNodeManagerBatch batch = await PrepareCoreAsync(
                    changes, default, cancellationToken, validationOnly: true)
                    .ConfigureAwait(false);
                await using var lifetime = batch.ConfigureAwait(false);
                if (batch is not PreparedBatch candidate)
                {
                    throw new InvalidOperationException("Validation requires the captured lifecycle batch owner.");
                }
                using (((IDynamicNodeManagerBatchHost)candidate.Host).UseTypeImage(
                    candidate.TypeTree, candidate.Factory))
                {
                    await inspectAsync(batch, cancellationToken).ConfigureAwait(false);
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
            private async ValueTask<IPreparedNodeManagerBatch> PrepareCoreAsync(
                ArrayOf<NodeManagerBatchChange> changes,
                ArrayOf<INodeManagerReadImage> images,
                CancellationToken cancellationToken,
                bool validationOnly = false)
            {
                lock (owner.m_operationLifetimeLock)
                {
                    if (m_closing || !ReferenceEquals(owner.m_publicationCapture, Capture) || !IsCurrent)
                    {
                        throw new InvalidOperationException("The publication invocation is stale or closed.");
                    }
                    if (m_preparing)
                    {
                        throw new InvalidOperationException("Prepare invocation units sequentially.");
                    }
                    m_preparing = true;
                    m_prepared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                try
                {
                    IPreparedNodeManagerBatch batch = await owner.PrepareBatchAsync(
                        changes, this, callerContext, cancellationToken, images, validationOnly)
                        .ConfigureAwait(false);
                    lock (owner.m_operationLifetimeLock)
                    {
                        m_batches.Add(batch);
                    }
                    return batch;
                }
                finally
                {
                    lock (owner.m_operationLifetimeLock)
                    {
                        m_preparing = false;
                        m_prepared.TrySetResult(true);
                    }
                }
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                Task? prepared;
                bool ownsDisposal;
                lock (owner.m_operationLifetimeLock)
                {
                    ownsDisposal = !m_closing;
                    m_closing = true;
                    prepared = m_preparing ? m_prepared.Task : null;
                }
                if (!ownsDisposal)
                {
                    await Finished.ConfigureAwait(false);
                    return;
                }
                try
                {
                    if (prepared is not null)
                    {
                        await prepared.ConfigureAwait(false);
                    }
                    var failures = new List<Exception>();
                    foreach (IPreparedNodeManagerBatch batch in m_batches)
                    {
                        try
                        {
                            await batch.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception failure) when (failure is not OutOfMemoryException)
                        {
                            failures.Add(failure);
                        }
                    }
                    if (failures.Count != 0)
                    {
                        throw new AggregateException("Publication invocation cleanup failed.", failures);
                    }
                }
                finally
                {
                    lock (owner.m_operationLifetimeLock)
                    {
                        owner.m_publicationCapture = null;
                        owner.m_publicationFinished = null;
                    }
                    m_operation.Dispose();
                    m_finished.TrySetResult(true);
                    owner.ScheduleRetiredGenerationDrainCleanup();
                }
            }

            private readonly List<IPreparedNodeManagerBatch> m_batches = [];
            private readonly OperationLifetime m_operation = operation;
            private readonly TaskCompletionSource<bool> m_finished = finished;
            private TaskCompletionSource<bool> m_prepared =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private bool m_preparing;
            private bool m_closing;
        }


        private readonly StandardServer m_server;
        private readonly SemaphoreSlim m_lifecycleSemaphore = new(1, 1);
        private readonly Lock m_registrationLock = new();
        private readonly Lock m_operationLifetimeLock = new();
        private readonly Dictionary<Guid, RegistrationState> m_registrations = [];
        private readonly List<RetiredNodeManager> m_retiredNodeManagers = [];

        private readonly BackgroundTaskScope m_backgroundWork =
            new(nameof(NodeManagerLifecycle), AmbientMessageContext.Telemetry);

        private TaskCompletionSource<bool>? m_operationsDrained;
        private int m_activeLifecycleOperations;
        private int m_activeShutdownMethods;
        private long m_shutdownCleanupProgress;
        private bool m_shutdownPrepared;
        private bool m_disposeRequested;
        private bool m_lifecycleSemaphoreDisposed;
        private volatile bool m_disposed;
        private volatile bool m_shuttingDown;
        private readonly AsyncLocal<OperationLifetime?> m_currentOperation = new();
        private PublicationCapture? m_publicationCapture;
        private TaskCompletionSource<bool>? m_publicationFinished;
    }
}
