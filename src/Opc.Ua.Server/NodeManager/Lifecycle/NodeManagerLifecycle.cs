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
                            .Select(state => state.Registration.Slot)
                            .Where(slot => slot.TryGetCurrent(out _))
                            .ToArray());
                }
            }
        }

        /// <inheritdoc/>
        public bool IsShuttingDown => m_shuttingDown;

        /// <inheritdoc/>
        public bool SupportsPreparedPublication =>
            m_server.CurrentInstance.NodeManager is IDynamicNodeManagerBatchHost;

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
            ThrowIfDecisionCallbackActive();

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
            ThrowIfDecisionCallbackActive();

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
                    RegistrationState[] registrations;
                    RetiredNodeManager[] retired;
                    lock (m_registrationLock)
                    {
                        registrations = [.. m_registrations.Values];
                        retired = [.. m_retiredNodeManagers];
                    }

                    foreach (RegistrationState registration in registrations)
                    {
                        host.CloseNodeManagerGenerationAdmissions(
                            registration.Prepared.NodeManager);
                    }

                    foreach (RetiredNodeManager retiredNodeManager in retired)
                    {
                        host.CloseNodeManagerGenerationAdmissions(
                            retiredNodeManager.NodeManager);
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
                await m_backgroundWork.DisposeAsync()
                    .AsTask()
                    .WaitAsync(ct)
                    .ConfigureAwait(false);
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
                        await CleanupShutdownNodeManagerAsync(
                                server,
                                host,
                                registration.Prepared.NodeManager,
                                cleanup,
                                pendingReferences: null,
                                destroyAddressSpace: false,
                                removeDestroyedExternalReferences: false,
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
            CancellationToken ct = default)
        {
            return AddAsync(factory, callerContext: null, ct: ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> AddAsync(
            INodeManagerFactory factory,
            CancellationToken ct = default)
        {
            return AddAsync(factory, callerContext: null, ct: ct);
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

            bool allowRequestCallback = IsRequestCallbackSafe(factory);
            _ = GetRunningServer(allowRequestCallback, callerContext);
            ValidateFactoryRegistration(factory, null);
            return AddCoreAsync(
                factory.CreateAsync,
                allowRequestCallback,
                factory.NamespacesUris,
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

            _ = GetRunningServer(allowRequestCallback: false, callerContext);
            return AddCoreAsync(
                (server, configuration, _) => new ValueTask<IAsyncNodeManager>(
                    factory.Create(server, configuration).ToAsyncNodeManager()),
                allowRequestCallback: false,
                factory.NamespacesUris,
                callerContext,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            IAsyncNodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return ReloadAsync(registration, replacement, callerContext: null, ct: ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> ReloadAsync(
            NodeManagerRegistration registration,
            INodeManagerFactory replacement,
            CancellationToken ct = default)
        {
            return ReloadAsync(registration, replacement, callerContext: null, ct: ct);
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
                namespacesUris: default,
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
                namespacesUris: default,
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
                ReloadRetirementMode.Migrate,
                allowRequestCallback: IsRequestCallbackSafe(replacement),
                namespacesUris: default,
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
                ReloadRetirementMode.Migrate,
                allowRequestCallback: false,
                namespacesUris: default,
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
                ReloadRetirementMode.Invalidate,
                allowRequestCallback: IsRequestCallbackSafe(replacement),
                replacement.NamespacesUris,
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
                ReloadRetirementMode.Invalidate,
                allowRequestCallback: false,
                replacement.NamespacesUris,
                callerContext: null,
                ct);
        }

        /// <inheritdoc/>
        public ValueTask RemoveAsync(
            NodeManagerRegistration registration,
            CancellationToken ct = default)
        {
            return RemoveAsync(registration, callerContext: null, ct: ct);
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
            IPreparedNodeManagerBatch batch = await PrepareBatchAsync(
                [NodeManagerBatchChange.Remove(registration)],
                publication: null,
                callerContext,
                ct).ConfigureAwait(false);
            await using (batch.ConfigureAwait(false))
            {
                NodeManagerBatchResult result = await batch
                    .CommitAsync(static _ => default, ct)
                    .ConfigureAwait(false);
                Exception? failure = result.CleanupFailure;
                if (result.Retirement.IsCompleted)
                {
                    try
                    {
                        await result.Retirement.ConfigureAwait(false);
                    }
                    catch (Exception retirementFailure) when (retirementFailure is not OutOfMemoryException)
                    {
                        failure ??= retirementFailure;
                    }
                }
                else
                {
                    ObserveRetirement(result.Retirement);
                }
                if (failure is not null)
                {
                    throw new NodeManagerRemovalCommittedException(registration, result, failure);
                }
            }
        }

        private static void ObserveRetirement(Task retirement)
        {
            _ = retirement.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private async ValueTask<NodeManagerRegistration> AddCoreAsync(
            CreateNodeManagerAsync createNodeManager,
            bool allowRequestCallback,
            ArrayOf<string> namespacesUris,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            var factory = new LifecycleNodeManagerFactory(
                createNodeManager,
                namespacesUris,
                allowRequestCallback);
            IPreparedNodeManagerBatch batch = await PrepareBatchAsync(
                [NodeManagerBatchChange.Add(factory)],
                publication: null,
                callerContext,
                ct,
                retainWriter: true).ConfigureAwait(false);
            await using (batch.ConfigureAwait(false))
            {
                NodeManagerBatchResult result = await batch
                    .CommitAsync(static _ => default, ct)
                    .ConfigureAwait(false);
                NodeManagerRegistration registration = result.Registrations[0];
                if (result.CleanupFailure is { } failure)
                {
                    if (failure is AggregateException aggregateFailure &&
                        aggregateFailure.Flatten().InnerExceptions.Count == 1)
                    {
                        failure = aggregateFailure.Flatten().InnerExceptions[0];
                    }
                    throw new NodeManagerAddCommittedException(
                        registration,
                        "The NodeManager was added, but post-commit binding, readiness or notification failed. " +
                        "The published generation was retained and remains available from Registrations.",
                        failure);
                }
                return registration;
            }
        }

        /// <summary>
        /// Replaces a registration through the prepared publication pipeline.
        /// </summary>
        /// <param name="registration">The registration to replace.</param>
        /// <param name="createNodeManager">The callback creating the replacement manager.</param>
        /// <param name="namespacesUris">The namespaces declared by the replacement factory.</param>
        /// <param name="allowRequestCallback">Whether the factory permits callback-originated lifecycle work.</param>
        /// <param name="callerContext">The calling operation context, if any.</param>
        /// <param name="transitionMode">How monitored items owned by the retiring generation are handled.</param>
        /// <param name="ct">The token used to cancel preparation or decision.</param>
        /// <returns>The committed replacement registration.</returns>
        private async ValueTask<NodeManagerRegistration> ReloadThroughBatchAsync(
            NodeManagerRegistration registration,
            CreateNodeManagerAsync createNodeManager,
            ArrayOf<string> namespacesUris,
            bool allowRequestCallback,
            IOperationContext? callerContext,
            NodeManagerMonitoredItemTransitionMode transitionMode,
            CancellationToken ct)
        {
            var factory = new LifecycleNodeManagerFactory(
                createNodeManager,
                namespacesUris,
                allowRequestCallback);
            IPreparedNodeManagerBatch batch = await PrepareBatchAsync(
                [NodeManagerBatchChange.Replace(
                    registration,
                    factory,
                    transitionMode)],
                publication: null,
                callerContext,
                ct,
                retainWriter: true).ConfigureAwait(false);
            await using (batch.ConfigureAwait(false))
            {
                NodeManagerBatchResult result = await batch
                    .CommitAsync(static _ => default, ct)
                    .ConfigureAwait(false);
                NodeManagerRegistration replacement = result.Registrations[0];
                if (result.CleanupFailure is { } failure)
                {
                    if (failure is AggregateException aggregateFailure &&
                        aggregateFailure.Flatten().InnerExceptions.Count == 1)
                    {
                        failure = aggregateFailure.Flatten().InnerExceptions[0];
                    }
                    throw new NodeManagerReloadCommittedException(
                        replacement,
                        "The replacement NodeManager is live and the replacement generation was retained, but " +
                        "post-commit reconciliation failed. A later lifecycle operation will retry generation cleanup.",
                        failure);
                }
                return replacement;
            }
        }

        private ValueTask<NodeManagerRegistration> ReloadCoreAsync(
            NodeManagerRegistration registration,
            CreateNodeManagerAsync createNodeManager,
            ReloadRetirementMode retirementMode,
            bool allowRequestCallback,
            ArrayOf<string> namespacesUris,
            IOperationContext? callerContext,
            CancellationToken ct = default)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }
            return ReloadThroughBatchAsync(
                registration,
                createNodeManager,
                namespacesUris,
                allowRequestCallback,
                callerContext,
                retirementMode == ReloadRetirementMode.Migrate
                    ? NodeManagerMonitoredItemTransitionMode.Migrate
                    : NodeManagerMonitoredItemTransitionMode.Invalidate,
                ct);
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
            if (IsExecutingRequest(server, callerContext) &&
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

        /// <summary>
        /// Enters a lifecycle operation that is tracked until it is disposed.
        /// </summary>
        /// <param name="publication">The publication capture that owns admission, or null for a regular
        /// operation.</param>
        /// <returns>The scope that ends the operation when disposed.</returns>
        private OperationLifetime EnterLifecycleOperation(PublicationCapture? publication = null)
        {
            ThrowIfDecisionCallbackActive();

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

                m_activeBackgroundOperations++;
                return true;
            }
        }

        private void ExitBackgroundOperation()
        {
            bool disposeSemaphore;
            lock (m_operationLifetimeLock)
            {
                m_activeBackgroundOperations--;
                disposeSemaphore = TryReserveSemaphoreDisposal();
            }
            if (disposeSemaphore)
            {
                m_lifecycleSemaphore.Dispose();
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
                m_activeBackgroundOperations != 0 ||
                m_activeShutdownMethods != 0 ||
                m_shutdownPrepared)
            {
                return false;
            }

            m_lifecycleSemaphoreDisposed = true;
            return true;
        }

        /// <summary>
        /// Releases the lifecycle semaphore while one retired generation's view epochs and
        /// background admissions drain, then reacquires it before returning.
        /// </summary>
        private async ValueTask DrainGenerationOutsideLifecycleSemaphoreAsync(
            IServerInternal server,
            NodeManagerGeneration generation,
            Task? transitionFinalization,
            CancellationToken ct)
        {
            m_lifecycleSemaphore.Release();
            try
            {
                await generation.WaitForQuiescenceAsync(ct)
                    .ConfigureAwait(false);
                if (transitionFinalization is not null)
                {
                    // The failure is observed by the caller from the completed task.
                    await transitionFinalization.ContinueWith(
                        static _ => { },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default).WaitAsync(ct).ConfigureAwait(false);
                }
            }
            finally
            {
                await m_lifecycleSemaphore
                    .WaitAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Starts finalizing the committed monitored-item transition of a retired generation once. The
        /// finalization waits for notifications admitted on the transitioned nodes and then marks deleted items.
        /// It runs without the caller's execution context so it never inherits a request or callback admission.
        /// </summary>
        /// <param name="retiredNodeManager">The retired generation.</param>
        private static void StartTransitionFinalization(RetiredNodeManager retiredNodeManager)
        {
            if (retiredNodeManager.Transition is not { } transition ||
                retiredNodeManager.TransitionFinalization is not null)
            {
                return;
            }

            bool restoreFlow = false;
            try
            {
                if (!ExecutionContext.IsFlowSuppressed())
                {
                    ExecutionContext.SuppressFlow();
                    restoreFlow = true;
                }
                retiredNodeManager.TransitionFinalization = Task.Run(async () =>
                {
                    await transition.WaitForPendingNotificationsAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    transition.MarkDeletedItems();
                });
            }
            finally
            {
                if (restoreFlow)
                {
                    ExecutionContext.RestoreFlow();
                }
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
            if (IsExecutingRequest(m_server.CurrentInstance, callerContext) &&
                !allowRequestCallback)
            {
                throw new InvalidOperationException(
                    "NodeManager lifecycle operations cannot run from an OPC UA request callback.");
            }
        }

        private static bool IsExecutingRequest(
            IServerInternal server,
            IOperationContext? callerContext)
        {
            return callerContext is null
                ? server.RequestManager.IsCurrentRequestExecuting()
                : server.RequestManager.IsExecutingRequest(callerContext);
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

        private RegistrationState GetCurrentState(NodeManagerRegistration registration)
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
                if (state.ReadinessPending)
                {
                    throw new InvalidOperationException(
                        "The NodeManager is completing readiness. Retry after its startup operation completes.");
                }
                return state;
            }
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
        /// Determines whether a retired node manager is still used by monitored items.
        /// </summary>
        /// <param name="server">The running server.</param>
        /// <param name="nodeManager">The retired node manager.</param>
        /// <returns>True if monitored items still use the node manager.</returns>
        private bool HasRetainedUses(IServerInternal server, IAsyncNodeManager nodeManager)
        {
            return HasActiveMonitoredItems(server, nodeManager);
        }

        private static void InvalidateContinuationPoints(
            IServerInternal server,
            NodeManagerGeneration generation)
        {
            foreach (ISession session in server.SessionManager.GetSessions())
            {
                if (session.ContinuationPoints is SessionContinuationPoints points)
                {
                    points.RemoveForGeneration(generation);
                }
            }
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
            host?.CloseNodeManagerGenerationAdmissions(nodeManager);

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
                    ChangePreparer.Detach(nodeManager);
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
                retired.CompleteRetirement();
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

        /// <summary>
        /// Schedules a background pass that disposes any shadow-retired generation whose
        /// monitored items and Browse continuations have drained. Invoked from an
        /// ownership-sensitive request or continuation disposal, so
        /// the teardown must never run inline on the request path: it is dispatched to the
        /// thread pool with the request's execution context suppressed. Background and direct
        /// cleanup use the same claim protocol: briefly take the lifecycle semaphore to claim
        /// the retired generation and invalidate immediate continuation points, release it for
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
                    ExitBackgroundOperation();
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
                    ExitBackgroundOperation();
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
                m_server.CurrentInstance.Telemetry
                    .CreateLogger<NodeManagerLifecycle>()
                    .RetiredGenerationCleanupFailed(ex);
            }
            finally
            {
                if (semaphoreHeld)
                {
                    m_lifecycleSemaphore.Release();
                }
                ExitBackgroundOperation();
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

        /// <summary>
        /// Cleans up retired generations. Opportunistic callers pass <paramref name="waitForDrain"/> as
        /// <c>false</c>: a lifecycle call can itself run inside work admitted by a retiring generation (a sampling
        /// or method callback), so it only cleans generations that are already quiescent and never waits for a
        /// drain that its own admission prevents. The scheduled background drain owns the waiting.
        /// </summary>
        /// <param name="server">The server that owns the retired generations.</param>
        /// <param name="host">The dynamic NodeManager host.</param>
        /// <param name="allowShuttingDown">Whether cleanup may continue during shutdown.</param>
        /// <param name="waitForDrain">Whether cleanup may wait for admitted work to drain.</param>
        /// <returns>A task that completes when the claimed generations were processed.</returns>
        private async ValueTask CleanupRetiredNodeManagersAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            bool allowShuttingDown = false,
            bool waitForDrain = true)
        {
            while (true)
            {
                RetiredNodeManager[] retired;
                lock (m_registrationLock)
                {
                    retired = [.. m_retiredNodeManagers];
                }

                // Each retired generation is claimed independently, so a generation whose cleanup is still running
                // (for example a destruction callback that removes a dependent registration) never delays the
                // cleanup of an unrelated generation.
                var claimed = new List<RetiredNodeManager>();
                foreach (RetiredNodeManager retiredNodeManager in retired)
                {
                    if (retiredNodeManager.DrainPending ||
                        (!waitForDrain && !retiredNodeManager.Generation.IsQuiescent) ||
                        (retiredNodeManager.AllowActiveMonitoredItems &&
                            HasRetainedUses(
                                server,
                                retiredNodeManager.NodeManager)))
                    {
                        continue;
                    }

                    lock (m_registrationLock)
                    {
                        if (retiredNodeManager.DrainPending)
                        {
                            continue;
                        }
                        retiredNodeManager.DrainPending = true;
                    }
                    retiredNodeManager.RequestsDrained = false;
                    claimed.Add(retiredNodeManager);
                }

                if (claimed.Count == 0)
                {
                    return;
                }

                try
                {
                    List<Exception>? cleanupFailures = null;
                    var drainReady = new List<RetiredNodeManager>();
                    foreach (RetiredNodeManager retiredNodeManager in claimed)
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
                            continue;
                        }
                        if (retiredNodeManager.NeedsDetachment && !retiredNodeManager.AllowActiveMonitoredItems)
                        {
                            InvalidateContinuationPoints(
                                server,
                                retiredNodeManager.Generation);
                        }
                        drainReady.Add(retiredNodeManager);
                    }

                    if (drainReady.Count == 0)
                    {
                        return;
                    }

                    foreach (RetiredNodeManager retiredNodeManager in drainReady)
                    {
                        StartTransitionFinalization(retiredNodeManager);
                        await DrainGenerationOutsideLifecycleSemaphoreAsync(
                                server,
                                retiredNodeManager.Generation,
                                retiredNodeManager.TransitionFinalization,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                        retiredNodeManager.RequestsDrained = true;
                        if (retiredNodeManager.TransitionFinalization is { IsFaulted: true } finalization)
                        {
                            (cleanupFailures ??= []).Add(finalization.Exception!.GetBaseException());
                        }
                        retiredNodeManager.Transition = null;
                        retiredNodeManager.TransitionFinalization = null;
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
                            retiredNodeManager.RequestsDrained = false;
                            continue;
                        }

                        if (retiredNodeManager.LateMigrationTarget is { } migrationTarget &&
                            !retiredNodeManager.LateMonitoredItemsMigrated)
                        {
                            await MigrateLateMonitoredItemsAsync(
                                server,
                                retiredNodeManager.NodeManager,
                                migrationTarget).ConfigureAwait(false);
                            retiredNodeManager.LateMonitoredItemsMigrated = true;
                        }

                        bool cleaned;
                        try
                        {
                            cleaned = await CleanupRetiredNodeManagerAsync(
                                server,
                                host,
                                retiredNodeManager,
                                allowShuttingDown).ConfigureAwait(false);
                        }
                        catch (Exception failure) when (failure is not OutOfMemoryException)
                        {
                            // One failing retired source must not prevent cleanup of later ones.
                            (cleanupFailures ??= []).Add(failure);
                            continue;
                        }
                        if (cleaned)
                        {
                            RemoveRetiredNodeManagerRecord(retiredNodeManager);
                        }
                    }
                    if (cleanupFailures is not null)
                    {
                        if (cleanupFailures.Count == 1)
                        {
                            ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
                        }
                        throw new AggregateException(
                            "Retired NodeManager cleanup failed.", cleanupFailures);
                    }
                }
                finally
                {
                    foreach (RetiredNodeManager retiredNodeManager in claimed)
                    {
                        retiredNodeManager.DrainPending = false;
                    }
                }
            }
        }

        /// <summary>
        /// Runs retired-generation cleanup and publishes asynchronous failures to its retirement task.
        /// </summary>
        /// <param name="server">The server that owns the retired generation.</param>
        /// <param name="host">The dynamic NodeManager host.</param>
        /// <param name="retired">The generation to clean up.</param>
        /// <param name="allowShuttingDown">Whether cleanup may continue during shutdown.</param>
        /// <returns>Whether cleanup completed.</returns>
        private async ValueTask<bool> CleanupRetiredNodeManagerAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            RetiredNodeManager retired,
            bool allowShuttingDown = false)
        {
            try
            {
                return await CleanupRetiredNodeManagerCoreAsync(
                    server, host, retired, allowShuttingDown).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                retired.FailRetirement(failure);
                throw;
            }
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
        private async ValueTask<bool> CleanupRetiredNodeManagerCoreAsync(
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

                if (!retired.RequestsDrained)
                {
                    throw new InvalidOperationException(
                        "A retired NodeManager cannot be detached before its requests drain.");
                }
                InvalidateContinuationPoints(server, retired.Generation);

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
                if (!cleanup.NotificationsFinalized)
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
                await GetGenerationReconciler(server).UnbindRetiredAsync(
                    retired.Generation,
                    unsubscribeAllEvents: false,
                    CancellationToken.None)
                    .ConfigureAwait(false);
                (host as IDynamicNodeManagerBatchHost)?.ReleaseRetiringEventGeneration(retired.Generation);
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

            if (!cleanup.DestroyedExternalReferencesRemoved)
            {
                await host.RemoveDestroyedExternalReferencesAsync(
                    retired.NodeManager,
                    CancellationToken.None).ConfigureAwait(false);
                cleanup.DestroyedExternalReferencesRemoved = true;
                RecordShutdownCleanupProgress();
            }

            if (!cleanup.Disposed)
            {
                (host as IDynamicNodeManagerBatchHost)?.ReleaseRetiringEventGeneration(retired.Generation);
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
        /// Carries a legacy factory callback into the prepared lifecycle transaction.
        /// </summary>
        private sealed class LifecycleNodeManagerFactory :
            IAsyncNodeManagerFactory,
            IRequestCallbackSafeNodeManagerFactory
        {
            /// <summary>
            /// Creates an adapter for the existing factory callback.
            /// </summary>
            /// <param name="createNodeManager">The callback creating the candidate manager.</param>
            /// <param name="namespacesUris">The candidate manager's declared namespaces.</param>
            /// <param name="allowRequestCallback">Whether the factory permits callback-originated lifecycle work.</param>
            public LifecycleNodeManagerFactory(
                CreateNodeManagerAsync createNodeManager,
                ArrayOf<string> namespacesUris,
                bool allowRequestCallback)
            {
                m_createNodeManager = createNodeManager;
                NamespacesUris = namespacesUris;
                AllowLifecycleFromRequestCallback = allowRequestCallback;
            }

            /// <summary>
            /// Gets the namespaces declared by the wrapped factory.
            /// </summary>
            public ArrayOf<string> NamespacesUris { get; }

            /// <summary>
            /// Gets whether the wrapped factory permits callback-originated lifecycle work.
            /// </summary>
            public bool AllowLifecycleFromRequestCallback { get; }

            /// <summary>
            /// Creates the NodeManager using the wrapped callback.
            /// </summary>
            /// <param name="server">The staging server.</param>
            /// <param name="configuration">The application configuration.</param>
            /// <param name="cancellationToken">The token to cancel candidate creation.</param>
            /// <returns>The prepared candidate manager.</returns>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return m_createNodeManager(server, configuration, cancellationToken);
            }

            /// <summary>The factory callback being adapted.</summary>
            private readonly CreateNodeManagerAsync m_createNodeManager;
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
                var sourceMonitoredNodeDrains = new List<Func<CancellationToken, ValueTask>>();
                var sourceRetentions = new List<Action>();
                var retainedNodeIds = new HashSet<NodeId>();
                foreach (IMonitoredItem monitoredItem in compatibleItems.Concat(deletedItems))
                {
                    if (!retainedNodeIds.Add(monitoredItem.NodeId))
                    {
                        continue;
                    }
                    if (m_current.TryRetainMonitoredNode(
                        monitoredItem.NodeId,
                        out Func<CancellationToken, ValueTask>? drain,
                        out Action? retention))
                    {
                        sourceMonitoredNodeDrains.Add(drain!);
                        sourceRetentions.Add(retention!);
                    }
                }
                m_sourceMonitoredNodeDrains = [.. sourceMonitoredNodeDrains];
                m_sourceRetentions = [.. sourceRetentions];
            }

            /// <summary>
            /// Waits for admitted notifications on the monitored nodes whose items are transitioning.
            /// </summary>
            /// <param name="ct">Cancels the wait.</param>
            /// <returns>A task that completes when the captured node consumers have drained.</returns>
            public async ValueTask WaitForPendingNotificationsAsync(CancellationToken ct)
            {
                if (Volatile.Read(ref m_sourceRetentions) is null)
                {
                    return;
                }
                try
                {
                    foreach (Func<CancellationToken, ValueTask> drain in m_sourceMonitoredNodeDrains)
                    {
                        await drain(ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    ReleaseSourceRetentions();
                }
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
                try
                {
                    var failures = new List<Exception>();
                    foreach (IMonitoredItem monitoredItem in m_deletedItems.Concat(m_failedItems))
                    {
                        if (m_markedDeletedItems.Contains(monitoredItem) || !IsOwnedBySubscription(monitoredItem))
                        {
                            continue;
                        }

                        try
                        {
                            var lifecycle = (IDetachableMonitoredItem)monitoredItem;
                            lifecycle.Detach(m_server);
                            lifecycle.MarkNodeDeleted();
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
                finally
                {
                    ReleaseSourceRetentions();
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

                ReleaseSourceRetentions();
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

            /// <summary>
            /// Releases source-node retentions when publication was rejected before draining began.
            /// </summary>
            internal void ReleaseSourceRetentions()
            {
                Action[]? retentions = Interlocked.Exchange(ref m_sourceRetentions, null);
                if (retentions is null)
                {
                    return;
                }
                foreach (Action retention in retentions)
                {
                    retention();
                }
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
            private readonly HashSet<IMonitoredItem> m_markedDeletedItems = [];
            private Action[]? m_sourceRetentions;
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

            public bool TryRetainMonitoredNode(
                NodeId nodeId,
                out Func<CancellationToken, ValueTask>? drain,
                out Action? release)
            {
                drain = null;
                release = null;
                return false;
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
            Invalidate
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

            /// <summary>
            /// Gets or sets whether the readiness callback of the registration has not yet completed.
            /// </summary>
            public bool ReadinessPending { get; set; }

            public ShutdownCleanupState ShutdownCleanup { get; } = new();
        }

        private sealed class RetiredNodeManager
        {
            public RetiredNodeManager(
                NodeManagerGeneration generation,
                List<LocalReference> pendingReferences,
                bool needsDetachment,
                bool allowActiveMonitoredItems = false,
                bool detachActiveMonitoredItems = false)
            {
                Generation = generation ?? throw new ArgumentNullException(nameof(generation));
                NodeManager = generation.NodeManager;
                PendingReferences = pendingReferences;
                NeedsDetachment = needsDetachment;
                AllowActiveMonitoredItems = allowActiveMonitoredItems;
                DetachActiveMonitoredItems = detachActiveMonitoredItems;
            }

            public IAsyncNodeManager NodeManager { get; }

            /// <summary>
            /// Gets the exact generation whose view epochs and admitted callbacks must drain.
            /// </summary>
            public NodeManagerGeneration Generation { get; }

            public List<LocalReference> PendingReferences { get; }

            public bool NeedsDetachment { get; set; }

            /// <summary>
            /// Gets or sets whether all requests that could still call this generation
            /// have drained since generation admission and continuation points were cut off.
            /// </summary>
            public bool RequestsDrained { get; set; }

            /// <summary>
            /// Gets or sets whether a background or direct two-phase drain currently owns
            /// cleanup. Callback-safe lifecycle operations skip the generation until that
            /// pass revalidates.
            /// </summary>
            public bool DrainPending
            {
                get => Volatile.Read(ref m_drainPending);
                set => Volatile.Write(ref m_drainPending, value);
            }

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

            /// <summary>
            /// Gets or sets the committed monitored-item transition whose deleted items are marked once the
            /// notifications admitted on their monitored nodes have drained.
            /// </summary>
            public MonitoredItemTransition? Transition { get; set; }

            /// <summary>
            /// Gets or sets the running finalization of <see cref="Transition"/>. It waits only for the transitioned
            /// nodes, never for the whole generation, so unrelated admitted work does not delay invalidation.
            /// </summary>
            public Task? TransitionFinalization { get; set; }

            public IAsyncNodeManager? LateMigrationTarget { get; set; }

            public bool LateMonitoredItemsMigrated { get; set; }

            public ShutdownCleanupState ShutdownCleanup { get; } = new();

            public Task Retirement => m_retirementCompletion.Task;

            public void CompleteRetirement()
            {
                m_retirementCompletion.TrySetResult(true);
            }

            public void FailRetirement(Exception failure)
            {
                m_retirementCompletion.TrySetException(failure);
            }

            private readonly TaskCompletionSource<bool> m_retirementCompletion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Publishes drain ownership across lifecycle semaphore release/reacquisition.</summary>
            private bool m_drainPending;
        }

        private sealed class ShutdownCleanupState
        {
            public bool Detached { get; set; }

            public bool NotificationsFinalized { get; set; }

            public bool ReferencesRemoved { get; set; }

            public bool Destroyed { get; set; }

            public bool DestroyedExternalReferencesRemoved { get; set; }

            public bool Released { get; set; }

            public bool Disposed { get; set; }
        }

        private sealed class DecisionCallbackScope
        {
            /// <summary>
            /// Gets whether the durable decision callback is still running.
            /// </summary>
            public bool IsActive => Volatile.Read(ref m_active) != 0;

            /// <summary>
            /// Marks the callback as complete for execution contexts it spawned.
            /// </summary>
            public void Deactivate()
            {
                Volatile.Write(ref m_active, 0);
            }

            /// <summary>
            /// Indicates whether the durable decision callback is still running.
            /// </summary>
            private int m_active = 1;
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

            /// <summary>
            /// Activates this held operation in the current asynchronous execution context.
            /// </summary>
            /// <returns>A scope that restores the previous ambient operation.</returns>
            public IDisposable Activate()
            {
                NodeManagerLifecycle owner = Volatile.Read(ref m_owner) ??
                    throw new ObjectDisposedException(nameof(OperationLifetime));
                OperationLifetime? previous = owner.m_currentOperation.Value;
                owner.m_currentOperation.Value = this;
                return new AmbientScope(owner, previous);
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

            private sealed class AmbientScope(NodeManagerLifecycle owner, OperationLifetime? previous) : IDisposable
            {
                public void Dispose()
                {
                    owner.m_currentOperation.Value = previous;
                }
            }

            private NodeManagerLifecycle? m_owner;
        }

        /// <inheritdoc/>
        public ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default)
        {
            return PrepareBatchAsync(changes, null, callerContext: null, cancellationToken);
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
        /// <param name="retainWriter">Whether the prepared batch keeps the lifecycle writer
        /// until it is committed or disposed, so no other lifecycle operation can interleave.</param>
        /// <returns>The prepared batch, which must be disposed if it is not committed.</returns>
        private async ValueTask<IPreparedNodeManagerBatch> PrepareBatchAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            PublicationInvocation? publication,
            IOperationContext? callerContext,
            CancellationToken cancellationToken,
            ArrayOf<INodeManagerReadImage> readImages = default,
            bool validationOnly = false,
            bool retainWriter = false)
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
                if (host is not IDynamicNodeManagerBatchHost)
                {
                    throw new NotSupportedException("The server host does not support prepared publication.");
                }
                if (server.Factory is not EncodeableFactory servingFactory)
                {
                    throw new NotSupportedException("The server factory does not support prepared publication.");
                }
                using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? waiter =
                    EnterRequestLifecycleWaiter(server, callerContext);
                await WaitForLifecycleSemaphoreAsync(waiter, cancellationToken).ConfigureAwait(false);
                bool releaseWriter = true;
                ChangePreparer? preparation = null;
                try
                {
                    EnsureSameRunningServer(server, host, allowRequestCallback);
                    await CleanupRetiredNodeManagersAsync(server, host, waitForDrain: false).ConfigureAwait(false);
                    EnsureSameRunningServer(server, host, allowRequestCallback);
                    preparation = new ChangePreparer(server);
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
                            entry.Prepared = await host.PrepareAsync(entry.Manager, cancellationToken)
                                .ConfigureAwait(false);
                            entry.Prepared.Preparation = preparation;
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
                                await host.ReplaceAsync(
                                    current.Prepared.NodeManager, entry.Prepared,
                                    ct: cancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                                await host.PublishAsync(entry.Prepared, cancellationToken).ConfigureAwait(false);
                            }
                            resolver = await m_server.RefreshComplexTypesAsync(
                                stagingServer, entry.Manager, publishResolver: false, cancellationToken: cancellationToken)
                                .ConfigureAwait(false) ??
                                resolver;
                            var registration = new NodeManagerRegistration(
                                current?.Registration.Id ?? Guid.NewGuid(),
                                (current?.Registration.Generation ?? 0) + 1,
                                entry.Manager);
                            entry.Prepared.Generation = registration.Identity;
                            entry.Next = new RegistrationState(registration, entry.Prepared, allowRequestCallback)
                            {
                                ReadinessPending = entry.Manager is INodeManagerReadinessNotification
                            };
                        }
                        finally
                        {
                            stagingServer?.Dispose();
                        }
                    }
                    // Staging can register hidden namespace routes; freeze its completed routing image.
                    var prepared = new PreparedBatch(
                        this, server, host, entries, operation, namespaceCount, allowRequestCallback,
                        preparation.TypeTree, preparation.OriginalTypes,
                        preparation.TypeRevision, preparation.Factory, preparation.OriginalFactory,
                        preparation.FactoryRevision, resolver, validationOnly, callerContext, preparation);
                    if (readImages.Count != 0)
                    {
                        prepared.BindReadImages(readImages);
                    }
                    if (retainWriter)
                    {
                        // Direct Add/Reload are one writer operation: keep the captured metadata revision current.
                        prepared.RetainWriter();
                        releaseWriter = false;
                    }
                    operation = null;
                    return prepared;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
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
                    if (releaseWriter)
                    {
                        m_lifecycleSemaphore.Release();
                    }
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

            bool writerHeld = batch.TakeWriter();
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? waiter =
                writerHeld ? null : EnterRequestLifecycleWaiter(batch.Server);
            if (!writerHeld)
            {
                await WaitForLifecycleSemaphoreAsync(waiter, cancellationToken).ConfigureAwait(false);
            }
            var retired = new List<RetiredNodeManager>();
            var failures = new List<Exception>();
            var monitoredItemTransitions = new List<MonitoredItemTransition>();
            var semanticChanges = new List<SemanticChangeStructureDataType>();
            long publishedRevision = 0;
            NodeManagerSlot[] publishedSlots = [];
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
                        if (decideAsync is not null && entry.Manager is not null)
                        {
                            semanticChanges.AddRange(GetSemanticChanges(
                                entry.Current.Prepared.NodeManager,
                                entry.Manager));
                        }
                        retired.Add(new RetiredNodeManager(
                            entry.Current.Registration.Identity,
                            entry.DroppedReferences,
                            needsDetachment: true,
                            allowActiveMonitoredItems: false));
                        if (decideAsync is not null)
                        {
                            MonitoredItemTransition transition =
                                entry.Change.TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate &&
                                entry.Manager is { } replacement
                                    ? await PrepareMonitoredItemTransitionAsync(
                                        batch.Server,
                                        entry.Current.Prepared.NodeManager,
                                        replacement,
                                        cancellationToken).ConfigureAwait(false)
                                    : await PrepareMonitoredItemRemovalAsync(
                                        batch.Server,
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

                foreach (BatchEntry entry in batch.Entries)
                {
                    if (entry.Manager is null || entry.Prepared is null)
                    {
                        continue;
                    }
                    if (batch.Server is INodeIdFactoryProvider
                        { NodeIdFactory: INodeIdFactoryPolicy policy })
                    {
                        await policy.PrepareNodeManagerAsync(
                            batch.Server,
                            entry.Manager,
                            entry.Current?.Prepared.NodeManager,
                            cancellationToken).ConfigureAwait(false);
                    }
                }

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

                    int retiredCapacity = m_retiredNodeManagers.Count + retired.Count;
                    if (m_retiredNodeManagers.Capacity < retiredCapacity)
                    {
                        m_retiredNodeManagers.Capacity = retiredCapacity;
                    }
                }

                await ((IDynamicNodeManagerBatchHost)batch.Host).CommitBatchAsync(
                    new NodeManagerBatchContext(
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
                            : token => InvokeDecisionCallbackAsync(decideAsync, token),
                        ViewPublished: () => batch.IsCommitted = true,
                        Published: () =>
                        {
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
                                        m_registrations[entry.Next.Registration.Id] = entry.Next;
                                    }
                                }
                                m_retiredNodeManagers.AddRange(retired);
                                publishedRevision =
                                    ((IDynamicNodeManagerBatchHost)batch.Host).RoutingRevision.Revision;
                                publishedSlots =
                                [
                                    .. m_registrations.Values
                                        .Select(state => state.Registration.Slot)
                                        .Where(slot => slot.TryGetCurrent(out _))
                                ];
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
                            // Retired generations are cut off by their gate; physical source bindings are released
                            // by retirement cleanup once admitted work has drained.
                            foreach (MonitoredItemTransition transition in monitoredItemTransitions)
                            {
                                try
                                {
                                    await transition.DetachCurrentAsync(CancellationToken.None).ConfigureAwait(false);
                                    failures.AddRange(await transition.AttachCompatibleAsync(CancellationToken.None)
                                        .ConfigureAwait(false));
                                }
                                catch (Exception failure) when (failure is not OutOfMemoryException)
                                {
                                    failures.Add(failure);
                                }
                            }
                            foreach (BatchEntry entry in batch.Entries)
                            {
                                if (entry.Manager is null)
                                {
                                    continue;
                                }
                                try
                                {
                                    if (entry.Prepared?.Generation is not { } generation)
                                    {
                                        throw new InvalidOperationException(
                                            "A committed NodeManager has no generation identity.");
                                    }
                                    if (!generation.Gate.TryEnter(out int admissionSlot))
                                    {
                                        continue;
                                    }
                                    m_lifecycleSemaphore.Release();
                                    try
                                    {
                                        GenerationReconciler reconciler = GetGenerationReconciler(batch.Server);
                                        GenerationReconciler.Bindings bindings = await reconciler.BindAsync(
                                            generation, CancellationToken.None).ConfigureAwait(false);
                                        await reconciler.ReconcileAsync(
                                            generation, bindings, CancellationToken.None).ConfigureAwait(false);
                                    }
                                    finally
                                    {
                                        generation.Gate.Exit(admissionSlot);
                                        await m_lifecycleSemaphore.WaitAsync(CancellationToken.None)
                                            .ConfigureAwait(false);
                                    }
                                }
                                catch (Exception failure) when (failure is not OutOfMemoryException)
                                {
                                    failures.Add(failure);
                                }
                            }
                        },
                        DeferReferenceRemoval: (owner, references) =>
                        {
                            RetiredNodeManager? retirement = retired.FirstOrDefault(item =>
                                ReferenceEquals(item.NodeManager, owner));
                            if (retirement is null)
                            {
                                throw new InvalidOperationException(
                                    "A deferred reference removal has no retiring NodeManager.");
                            }
                            retirement.PendingReferences.AddRange(references);
                        },
                        ReportCleanupFailure: failures.Add,
                        CancellationToken: cancellationToken,
                        Preparation: batch.Preparation)).ConfigureAwait(false);
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
                    NodeManagerGeneration generation = entry.Next.Registration.Identity;
                    if (!generation.Gate.TryEnter(out int admissionSlot))
                    {
                        ReleaseReadinessClaim(entry.Next);
                        continue;
                    }
                    try
                    {
                        if (entry.Change.TransitionMode == NodeManagerMonitoredItemTransitionMode.Migrate)
                        {
                            await RecoverDetachedMonitoredItemsAsync(
                                batch.Server, entry.Manager, CancellationToken.None).ConfigureAwait(false);
                        }
                        // A single-registration operation lets its caller cancel readiness; a prepared batch has
                        // already committed every change, so its readiness runs to completion.
                        await CompleteReadinessOutsideLifecycleSemaphoreAsync(
                            batch.Server,
                            batch.Host,
                            entry.Next,
                            writerHeld ? cancellationToken : CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        failures.Add(failure);
                    }
                    finally
                    {
                        generation.Gate.Exit(admissionSlot);
                        ReleaseReadinessClaim(entry.Next);
                    }
                }
                // Callbacks admitted before the cutoff may still deliver to transitioned items, so deleted items
                // are marked after their monitored nodes drained, outside the writer and without waiting for
                // unrelated work that holds the retired generation.
                foreach (RetiredNodeManager retiredNodeManager in retired)
                {
                    StartTransitionFinalization(retiredNodeManager);
                }
                batch.Host.SetRetiredGenerationDrainObserver(ScheduleRetiredGenerationDrainCleanup);
                try
                {
                    RetiredNodeManager[] pendingRetirements;
                    lock (m_registrationLock)
                    {
                        pendingRetirements = [.. m_retiredNodeManagers];
                    }
                    bool retirementCanCompleteInline = pendingRetirements.All(retiredNodeManager =>
                        retiredNodeManager.DrainPending ||
                        retiredNodeManager.Generation.IsQuiescent &&
                        (!retiredNodeManager.AllowActiveMonitoredItems ||
                            !HasRetainedUses(batch.Server, retiredNodeManager.NodeManager)));
                    if (retirementCanCompleteInline)
                    {
                        await CleanupRetiredNodeManagersAsync(batch.Server, batch.Host, waitForDrain: false)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        ScheduleRetiredGenerationDrainCleanup();
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    failures.Add(failure);
                }
                if (batch.Entries.Count != 0)
                {
                    string operation = batch.Entries.Count == 1
                        ? batch.Entries[0].Current is null
                            ? "added"
                            : batch.Entries[0].Next is null
                                ? "removed"
                                : "reloaded"
                        : "batch";
                    try
                    {
                        await NotifyCommittedChangeAsync(
                            batch.Server,
                            operation,
                            batch.NamespaceCount,
                            CancellationToken.None,
                            semanticChanges.ToArrayOf()).ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        failures.Add(failure);
                    }
                }
                uint completed;
                lock (m_registrationLock)
                {
                    completed = (uint)retired.Count(item => !m_retiredNodeManagers.Contains(item));
                }
                return new NodeManagerBatchResult(
                    batch.Registrations, completed,
                    failures.Count == 0 ? null : new AggregateException(
                        "The batch committed; generation reconciliation remains pending.", failures),
                    publishedRevision,
                    publishedSlots,
                    Task.WhenAll(retired.Select(item => item.Retirement)));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                var cleanupFailures = new List<Exception>();
                foreach (MonitoredItemTransition transition in monitoredItemTransitions)
                {
                    try
                    {
                        if (!batch.IsCommitted)
                        {
                            transition.ReleaseSourceRetentions();
                        }
                    }
                    catch (Exception cleanupFailure) when (cleanupFailure is not OutOfMemoryException)
                    {
                        cleanupFailures.Add(cleanupFailure);
                    }
                }
                if (batch.IsCommitted)
                {
                    // A committed transition still invalidates its deleted items.
                    foreach (RetiredNodeManager retiredNodeManager in retired)
                    {
                        StartTransitionFinalization(retiredNodeManager);
                    }
                }
                if (cleanupFailures.Count > 0)
                {
                    var failuresWithOriginal = new List<Exception> { failure };
                    failuresWithOriginal.AddRange(cleanupFailures);
                    throw new AggregateException(
                        "Batch completion failed and monitored-node notification cleanup also failed.",
                        failuresWithOriginal);
                }
                throw;
            }
            finally
            {
                m_lifecycleSemaphore.Release();
            }
        }

        private async ValueTask InvokeDecisionCallbackAsync(
            Func<CancellationToken, ValueTask> decideAsync,
            CancellationToken cancellationToken)
        {
            ThrowIfDecisionCallbackActive();

            DecisionCallbackScope? previous = m_decisionCallback.Value;
            var current = new DecisionCallbackScope();
            m_decisionCallback.Value = current;
            try
            {
                await decideAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                current.Deactivate();
                m_decisionCallback.Value = previous;
            }
        }

        /// <summary>
        /// Rejects lifecycle re-entry from the durable decision callback.
        /// </summary>
        private void ThrowIfDecisionCallbackActive()
        {
            if (m_decisionCallback.Value is { IsActive: true })
            {
                throw new InvalidOperationException(
                    "NodeManager lifecycle operations cannot be started from the durable decision callback.");
            }
        }

        /// <summary>
        /// Rolls back the entries of a batch that was not published by cleaning up prepared node managers and disposing
        /// created managers, in reverse order.
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
                        await host.RollbackAsync(entry.Prepared, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
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
            /// <param name="preparation">The explicit private staging metadata.</param>
            public PreparedBatch(
                NodeManagerLifecycle owner,
                IServerInternal server,
                IDynamicNodeManagerHost host,
                List<BatchEntry> entries,
                OperationLifetime operation,
                int namespaceCount,
                bool allowRequestCallback,
                TypeTable typeTree,
                TypeTable originalTypes,
                long typeRevision,
                EncodeableFactory factory,
                EncodeableFactory originalFactory,
                long factoryRevision,
                IDataTypeDefinitionResolver? resolver,
                bool validationOnly,
                IOperationContext? callerContext,
                ChangePreparer preparation)
            {
                m_owner = owner;
                CallerContext = callerContext;
                Preparation = preparation;
                Server = server;
                Host = host;
                Entries = entries;
                m_operation = operation;
                NamespaceCount = namespaceCount;
                AllowRequestCallback = allowRequestCallback;
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
            /// Transfers the held lifecycle writer to this batch.
            /// </summary>
            public void RetainWriter()
            {
                Volatile.Write(ref m_writerRetained, 1);
            }

            /// <summary>
            /// Takes ownership of a lifecycle writer retained from preparation.
            /// </summary>
            /// <returns>True when the caller now owns the held writer.</returns>
            public bool TakeWriter()
            {
                return Interlocked.Exchange(ref m_writerRetained, 0) != 0;
            }

            /// <summary>
            /// Gets the explicit staging metadata owned by this preparation.
            /// </summary>
            public ChangePreparer Preparation { get; }

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
                bool writerHeld = TakeWriter();
                try
                {
                    if (operation is null)
                    {
                        return;
                    }
                    using IDisposable ambientOperation = operation.Activate();
                    if (!IsCommitted)
                    {
                        if (!writerHeld)
                        {
                            await m_owner.m_lifecycleSemaphore.WaitAsync(CancellationToken.None)
                                .ConfigureAwait(false);
                            writerHeld = true;
                        }
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
                    if (writerHeld)
                    {
                        m_owner.m_lifecycleSemaphore.Release();
                    }
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
            private int m_writerRetained;
            private int m_state;
            private bool m_readImagesBound;
        }

        /// <inheritdoc/>
        public bool SupportsPublicationIsolation =>
            SupportsPreparedPublication && m_server.CurrentInstance.Factory is EncodeableFactory;

        /// <inheritdoc/>
        public INodeManagerPublicationCapture CapturePublication()
        {
            (IServerInternal server, IDynamicNodeManagerHost host) = GetRunningServer(allowRequestCallback: true);
            if (host is not IDynamicNodeManagerBatchHost batchHost || server.Factory is not EncodeableFactory factory)
            {
                throw new NotSupportedException("The server cannot capture publication revisions.");
            }
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
            ThrowIfDecisionCallbackActive();

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
                CancellationToken cancellationToken = default)
            {
                return BeginAsync(callerContext: null, cancellationToken);
            }

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
        private int m_activeBackgroundOperations;
        private int m_activeShutdownMethods;
        private long m_shutdownCleanupProgress;
        private bool m_shutdownPrepared;
        private bool m_disposeRequested;
        private bool m_lifecycleSemaphoreDisposed;
        private volatile bool m_disposed;
        private volatile bool m_shuttingDown;
        private readonly AsyncLocal<OperationLifetime?> m_currentOperation = new();

        /// <summary>
        /// Carries the active durable-decision scope through asynchronous callback execution.
        /// </summary>
        private readonly AsyncLocal<DecisionCallbackScope?> m_decisionCallback = new();
        private PublicationCapture? m_publicationCapture;
        private TaskCompletionSource<bool>? m_publicationFinished;
    }
}
