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
    public sealed class NodeManagerLifecycle : INodeManagerLifecycle, IDisposable
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

        internal async ValueTask BeginShutdownAsync(
            IServerInternal server,
            CancellationToken ct = default)
        {
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            Task activeOperations = EnterShutdownMethod();
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

            Task activeOperations = EnterShutdownMethod();
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
            (IServerInternal entryServer, _) =
                GetRunningServer(allowRequestCallback, callerContext);
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? requestWaiter =
                EnterRequestLifecycleWaiter(entryServer);
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
                                    CancellationToken.None).ConfigureAwait(false);
                                await CommitWithReconciliationAsync(
                                    server,
                                    host,
                                    state.Prepared,
                                    state.Prepared.NodeManager,
                                    rollbackBindings,
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
                        await host
                            .DestroyAddressSpaceAsync(
                                state.Prepared.NodeManager,
                                ct: CancellationToken.None)
                            .ConfigureAwait(false);
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
            (IServerInternal entryServer, _) =
                GetRunningServer(allowRequestCallback, callerContext);
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? requestWaiter =
                EnterRequestLifecycleWaiter(entryServer);
            await WaitForLifecycleSemaphoreAsync(requestWaiter, ct)
                .ConfigureAwait(false);
            IAsyncNodeManager? nodeManager = null;
            PreparedNodeManager? prepared = null;
            IServerInternal? server = null;
            IDynamicNodeManagerHost? host = null;
            int namespaceCountBefore = 0;
            bool committed = false;
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
                    .RefreshComplexTypesAsync(server, nodeManager, ct)
                    .ConfigureAwait(false);
                ServerBindings bindings = await BindToServerAsync(
                    server,
                    nodeManager,
                    ct).ConfigureAwait(false);
                await host.PublishAsync(prepared, ct).ConfigureAwait(false);

                await CommitWithReconciliationAsync(
                    server,
                    host,
                    prepared,
                    nodeManager,
                    bindings,
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
                lock (m_registrationLock)
                {
                    m_registrations.Add(
                        registration.Id,
                        new RegistrationState(
                            registration,
                            prepared,
                            allowRequestCallback));
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
                        CancellationToken.None).ConfigureAwait(false);
                }
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
                        "The NodeManager was added, but post-commit binding or notification failed. " +
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
                                    CancellationToken.None).ConfigureAwait(false);
                            await ReconcileBindingsAsync(
                                server,
                                nodeManager,
                                recoveryBindings,
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
            (IServerInternal entryServer, _) =
                GetRunningServer(allowRequestCallback, callerContext);
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? requestWaiter =
                EnterRequestLifecycleWaiter(entryServer);
            await WaitForLifecycleSemaphoreAsync(requestWaiter, ct)
                .ConfigureAwait(false);
            IAsyncNodeManager? replacementManager = null;
            PreparedNodeManager? replacement = null;
            RegistrationState? current = null;
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
                    .RefreshComplexTypesAsync(server, replacementManager, ct)
                    .ConfigureAwait(false);
                ServerBindings bindings = await BindToServerAsync(
                    server,
                    replacementManager,
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
                lock (m_registrationLock)
                {
                    m_registrations[current.Registration.Id] = new RegistrationState(
                        nextRegistration,
                        replacement,
                        allowRequestCallback);
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
                // shadow-retired generation's monitored items drain, rather than waiting for
                // the next lifecycle operation or server shutdown.
                if (deferForActiveMonitoredItems)
                {
                    host.SetRetiredGenerationDrainObserver(
                        ScheduleRetiredGenerationDrainCleanup);
                }

                bool retiredDrainClaimed =
                    !deferForActiveMonitoredItems ||
                    !HasActiveMonitoredItems(
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
                    await WaitForNotificationDispatchesOutsideLifecycleSemaphoreAsync(
                            server,
                            host,
                            retired.NodeManager)
                        .ConfigureAwait(false);
                    if (deferForActiveMonitoredItems &&
                        HasActiveMonitoredItems(server, retired.NodeManager))
                    {
                        host.SetRetiredGenerationNotifications(
                            retired.NodeManager,
                            enabled: true);
                        retired.NotificationsSuspended = false;
                        retired.DrainPending = false;
                        retiredDrainReady = false;
                    }
                    else
                    {
                        InvalidateContinuationPoints(server, retired.NodeManager);
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
                            CancellationToken.None).ConfigureAwait(false);
                    }

                    if (retiredDrainReady)
                    {
                        retired.RequestsDrained = true;
                        if (deferForActiveMonitoredItems &&
                            HasActiveMonitoredItems(
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
                                        CancellationToken.None).ConfigureAwait(false);
                                await ReconcileBindingsAsync(
                                    server,
                                    replacementManager,
                                    recoveryBindings,
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
                m_lifecycleSemaphore.Release();
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

            if (m_server.CurrentState != ServerState.Running)
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
            return (server, host);
        }

        private OperationLifetime EnterLifecycleOperation()
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

                m_activeLifecycleOperations++;
                return new OperationLifetime(this);
            }
        }

        private bool TryEnterBackgroundOperation()
        {
            lock (m_operationLifetimeLock)
            {
                if (m_disposed || m_shuttingDown)
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
                    m_server.CurrentState == ServerState.Running &&
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
            EnterRequestLifecycleWaiter(IServerInternal server)
        {
            RequestManagerLifecycleExtension? extension =
                server.RequestManager.LifecycleExtension;
            if (extension is null)
            {
                return null;
            }

            // The drain waiter correlates by the ambient request that is executing the
            // lifecycle operation, not by the explicit caller context. ShadowReloadAsync and
            // other internal callers pass a null caller context but still run inside a request
            // scope, so gating on the ambient request keeps them excluded from their own drain.
            uint? currentRequestId =
                server.RequestManager.GetCurrentRequestIdForLifecycleExtension();
            return currentRequestId.HasValue && currentRequestId.Value != uint.MaxValue
                ? extension.EnterLifecycleWaiter()
                : null;
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
            CancellationToken ct,
            Func<ValueTask>? beforeCommit = null,
            Func<ValueTask>? afterCommit = null,
            Func<ValueTask>? rollbackCommit = null)
        {
            await host.CommitAsync(
                prepared,
                async () =>
                {
                    if (beforeCommit is not null)
                    {
                        await beforeCommit().ConfigureAwait(false);
                    }
                    await ReconcileBindingsAsync(
                        server,
                        nodeManager,
                        bindings,
                        ct).ConfigureAwait(false);
                },
                afterCommit,
                rollbackCommit,
                ct).ConfigureAwait(false);
        }

        private static async ValueTask<ServerBindings> BindToServerAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
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
            CancellationToken ct)
        {
            using var context = new OperationContext(monitoredItem);
            try
            {
                await nodeManager
                    .SubscribeToAllEventsAsync(
                        context,
                        monitoredItem.SubscriptionId,
                        monitoredItem,
                        false,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                try
                {
                    await nodeManager
                        .SubscribeToAllEventsAsync(
                            context,
                            monitoredItem.SubscriptionId,
                            monitoredItem,
                            true,
                            CancellationToken.None)
                        .ConfigureAwait(false);
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

                if (definitionSource is not null &&
                    !definitionSource
                        .GetDataTypeDefinition(server.NamespaceUris)
                        .IsEqual(entry.Value))
                {
                    throw new InvalidOperationException(
                        $"DataType '{entry.Key}' has an incompatible definition. " +
                        "Runtime DataType definitions are immutable for the server lifetime.");
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
                    null!);
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

            Exception? unbindException = null;
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
                    unbindException = ex;
                    failures.Add(ex);
                }
            }

            Exception? rollbackException = null;
            try
            {
                await host
                    .RollbackAsync(prepared, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                rollbackException = ex;
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
        /// monitored items have drained. Invoked by the host from an ownership-sensitive
        /// monitored item request (for example, the Delete that drains the last item), so
        /// the teardown must never run inline on the request path: it is dispatched to the
        /// thread pool with the request's execution context suppressed. Background and direct
        /// cleanup use the same claim protocol: briefly take the lifecycle semaphore to suspend
        /// retired-generation notifications and invalidate continuation points, release it for
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
                m_backgroundWork.Run(
                    nameof(DrainRetiredGenerationsAsync),
                    async _ => await DrainRetiredGenerationsAsync().ConfigureAwait(false));
                scheduled = true;
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
                m_server.CurrentState != ServerState.Running)
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
                            HasActiveMonitoredItems(
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
                            HasActiveMonitoredItems(
                                server,
                                retiredNodeManager.NodeManager))
                        {
                            host.SetRetiredGenerationNotifications(
                                retiredNodeManager.NodeManager,
                                enabled: true);
                            retiredNodeManager.NotificationsSuspended = false;
                            continue;
                        }
                        if (retiredNodeManager.NeedsDetachment)
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
                            HasActiveMonitoredItems(
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

            if (!HasActiveMonitoredItems(server, retired.NodeManager))
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
        /// monitored items is left untouched (requests, continuation points, and
        /// monitored items that already captured it keep working) and <c>false</c> is
        /// returned so the caller retries cleanup on a later opportunity. An immediate
        /// retirement instead invalidates owned monitored items before detachment; neither
        /// policy deletes the client's subscription.
        /// </summary>
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
                    HasActiveMonitoredItems(server, retired.NodeManager))
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
                if (retired.DetachActiveMonitoredItems)
                {
                    await DetachActiveMonitoredItemsAsync(
                            server,
                            retired.NodeManager)
                        .ConfigureAwait(false);
                }
                EnsureNoActiveMonitoredItems(server, retired.NodeManager);
                if (retired.AllowActiveMonitoredItems &&
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
                    unsubscribeAllEvents: !retired.AllowActiveMonitoredItems)
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
                await host
                    .DestroyAddressSpaceAsync(
                        retired.NodeManager,
                        ct: CancellationToken.None)
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
            /// once requests using this generation have drained.
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
            public OperationLifetime(NodeManagerLifecycle owner)
            {
                m_owner = owner;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref m_owner, null)?.ExitLifecycleOperation();
            }

            private NodeManagerLifecycle? m_owner;
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
    }
}
