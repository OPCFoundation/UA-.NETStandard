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
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
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

            m_shuttingDown = true;
            await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (server.NodeManager is IDynamicNodeManagerHost host)
                {
                    await CleanupRetiredNodeManagersAsync(
                        server,
                        host).ConfigureAwait(false);
                }
            }
            finally
            {
                m_lifecycleSemaphore.Release();
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

            await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                RegistrationState[] registrations;
                RetiredNodeManager[] retired;
                lock (m_registrationLock)
                {
                    registrations = [.. m_registrations.Values];
                    m_registrations.Clear();
                    retired = [.. m_retiredNodeManagers];
                    m_retiredNodeManagers.Clear();
                }

                var host =
                    server.NodeManager as IDynamicNodeManagerHost;
                foreach (RegistrationState registration in registrations)
                {
                    host?.Release(registration.Prepared.NodeManager);
                    await DisposeNodeManagerAsync(registration.Prepared.NodeManager)
                        .ConfigureAwait(false);
                }

                // The server itself tears down every session, subscription, and
                // monitored item during shutdown, so a shadow-reloaded generation that
                // is still draining outside of shutdown is safe to dispose here rather
                // than left to leak.
                foreach (RetiredNodeManager retiredNodeManager in retired)
                {
                    host?.Release(retiredNodeManager.NodeManager);
                    await DisposeNodeManagerAsync(retiredNodeManager.NodeManager)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                m_lifecycleSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> AddAsync(
            IAsyncNodeManagerFactory factory,
            CancellationToken ct = default)
        {
            if (factory is null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return AddCoreAsync(
                factory.CreateAsync,
                IsRequestCallbackSafe(factory),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> AddAsync(
            INodeManagerFactory factory,
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
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> ReloadAsync(
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
                ReloadRetirementMode.Migrate,
                allowRequestCallback: IsRequestCallbackSafe(replacement),
                ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeManagerRegistration> ReloadAsync(
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
                ct);
        }

        /// <inheritdoc/>
        public async ValueTask RemoveAsync(
            NodeManagerRegistration registration,
            CancellationToken ct = default)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                RegistrationState state = GetCurrentState(registration);
                EnsureRequestCallbackAllowed(state.AllowLifecycleFromRequestCallback);
                (IServerInternal server, IDynamicNodeManagerHost host) = GetRunningServer(
                    state.AllowLifecycleFromRequestCallback);
                await CleanupRetiredNodeManagersAsync(server, host).ConfigureAwait(false);
                MonitoredItemTransition monitoredItemTransition =
                    await PrepareMonitoredItemRemovalAsync(
                        server,
                        state.Prepared.NodeManager,
                        ct).ConfigureAwait(false);
                if (!state.Prepared.Published &&
                    state.Prepared.Staged)
                {
                    await UnbindFromServerAsync(
                        server,
                        state.Prepared.NodeManager,
                        CancellationToken.None).ConfigureAwait(false);
                    state.Prepared.Staged = false;
                }
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

                    try
                    {
                        InvalidateContinuationPoints(
                            server,
                            state.Prepared.NodeManager);
                        await server.RequestManager
                            .WaitForCurrentRequestsAsync(ct)
                            .ConfigureAwait(false);
                        InvalidateContinuationPoints(
                            server,
                            state.Prepared.NodeManager);
                        await UnbindFromServerAsync(
                            server,
                            state.Prepared.NodeManager,
                            ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
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
                            await server.RequestManager
                                .WaitForCurrentRequestsAsync(CancellationToken.None)
                                .ConfigureAwait(false);
                            await ReconcileBindingsAsync(
                                server,
                                state.Prepared.NodeManager,
                                rollbackBindings,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception rollbackException) when (
                            rollbackException is not OutOfMemoryException)
                        {
                            Exception? cleanupException = null;
                            if (!state.Prepared.Published)
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
                }

                monitoredItemTransition.MarkDeletedItems();
                await host
                    .DestroyAsync(
                        state.Prepared.NodeManager,
                        removeExternalReferences: true,
                        ct: CancellationToken.None)
                    .ConfigureAwait(false);
                RebuildActiveTypeTree(server);
                await DisposeNodeManagerAsync(state.Prepared.NodeManager)
                    .ConfigureAwait(false);
                lock (m_registrationLock)
                {
                    m_registrations.Remove(registration.Id);
                }

                await NotifyCommittedChangeAsync(
                    server,
                    "removed",
                    namespaceCountBefore: server.NamespaceUris.Count,
                    ct: CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                m_lifecycleSemaphore.Release();
            }
        }

        private async ValueTask<NodeManagerRegistration> AddCoreAsync(
            CreateNodeManagerAsync createNodeManager,
            bool allowRequestCallback,
            CancellationToken ct)
        {
            EnsureRequestCallbackAllowed(allowRequestCallback);
            await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
            IAsyncNodeManager? nodeManager = null;
            PreparedNodeManager? prepared = null;
            IServerInternal? server = null;
            IDynamicNodeManagerHost? host = null;
            int namespaceCountBefore = 0;
            bool committed = false;
            try
            {
                (server, host) = GetRunningServer(allowRequestCallback);
                await CleanupRetiredNodeManagersAsync(server, host).ConfigureAwait(false);
                namespaceCountBefore = server.NamespaceUris.Count;
                nodeManager = await createNodeManager(
                    server,
                    m_server.CurrentConfiguration,
                    ct).ConfigureAwait(false) ??
                    throw new InvalidOperationException(
                        "The NodeManager factory returned null.");
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

                await server.RequestManager
                    .WaitForCurrentRequestsAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                await ReconcileBindingsAsync(
                    server,
                    nodeManager,
                    bindings,
                    CancellationToken.None).ConfigureAwait(false);
                await NotifyCommittedChangeAsync(
                    server,
                    "added",
                    namespaceCountBefore,
                    CancellationToken.None).ConfigureAwait(false);
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
                        prepared).ConfigureAwait(false);
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
                        await server.RequestManager
                            .WaitForCurrentRequestsAsync(CancellationToken.None)
                            .ConfigureAwait(false);
                        await ReconcileBindingsAsync(
                            server,
                            nodeManager,
                            recoveryBindings,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception recoveryFailure) when (
                        recoveryFailure is not OutOfMemoryException)
                    {
                        recoveryException = recoveryFailure;
                    }
                }

                if (server is not null)
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
                    prepared?.Published != true)
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
            CancellationToken ct)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
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
            ServiceResult? immediateRetirementError =
                retirementMode == ReloadRetirementMode.Immediate
                    ? new ServiceResult(StatusCodes.BadNodeIdUnknown)
                    : null;
            try
            {
                current = GetCurrentState(registration);
                allowRequestCallback &=
                    current.AllowLifecycleFromRequestCallback;
                EnsureRequestCallbackAllowed(allowRequestCallback);
                (server, host) = GetRunningServer(allowRequestCallback);
                await CleanupRetiredNodeManagersAsync(server, host).ConfigureAwait(false);
                namespaceCountBefore = server.NamespaceUris.Count;
                if (immediateRetirementError is not null)
                {
                    EnsureImmediateRetirementSupported(
                        server,
                        current.Prepared.NodeManager);
                }

                replacementManager = await createNodeManager(
                    server,
                    m_server.CurrentConfiguration,
                    ct).ConfigureAwait(false) ??
                    throw new InvalidOperationException(
                        "The replacement NodeManager factory returned null.");
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
                Func<ValueTask>? beforeReloadCommit = beforeTransitionCommit;
                if (immediateRetirementError is not null)
                {
                    beforeReloadCommit = () =>
                    {
                        EnsureImmediateRetirementSupported(
                            server,
                            current.Prepared.NodeManager);
                        return default;
                    };
                }
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
                        allowActiveMonitoredItems,
                        ct)
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
                    immediateRetirementError: immediateRetirementError);
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
                Exception? postCommitFailure = null;
                try
                {
                    await server.RequestManager
                        .WaitForCurrentRequestsAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    await ReconcileBindingsAsync(
                        server,
                        replacementManager,
                        bindings,
                        CancellationToken.None).ConfigureAwait(false);
                    bool cleaned = await CleanupRetiredNodeManagerAsync(server, host, retired)
                        .ConfigureAwait(false);
                    if (cleaned)
                    {
                        lock (m_registrationLock)
                        {
                            m_retiredNodeManagers.Remove(retired);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    postCommitFailure = ex;
                }

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
                        replacement).ConfigureAwait(false);
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
                                    immediateRetirementError: immediateRetirementError));
                        }

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
                            await server.RequestManager
                                .WaitForCurrentRequestsAsync(CancellationToken.None)
                                .ConfigureAwait(false);
                            await ReconcileBindingsAsync(
                                server,
                                replacementManager,
                                recoveryBindings,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception recoveryFailure) when (
                            recoveryFailure is not OutOfMemoryException)
                        {
                            recoveryException = recoveryFailure;
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

                if (server is not null)
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
                if (replacementManager is not null && replacement?.Published != true)
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
            bool allowRequestCallback = false)
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
            if (server.RequestManager.IsExecutingRequest &&
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

        private void EnsureRequestCallbackAllowed(bool allowRequestCallback)
        {
            if (m_server.CurrentState == ServerState.Running &&
                m_server.CurrentInstance.RequestManager.IsExecutingRequest &&
                !allowRequestCallback)
            {
                throw new InvalidOperationException(
                    "NodeManager lifecycle operations cannot run from an OPC UA request callback.");
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
            return ReferenceEquals(first, second) ||
                ReferenceEquals(first.SyncNodeManager, second.SyncNodeManager);
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

        private static void EnsureImmediateRetirementSupported(
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
                if (!tracker.HasMonitoredItems(nodeManager))
                {
                    continue;
                }
                if (subscription is not INodeManagerMonitoredItemRetirementTracker retirementTracker ||
                    !retirementTracker.CanRetireMonitoredItems(nodeManager))
                {
                    throw new NotSupportedException(
                        "The configured subscription cannot retire NodeManager-owned monitored items.");
                }
            }
        }

        private static ValueTask RetireMonitoredItemsAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            ServiceResult error,
            bool detachOwner = false)
        {
            RetireMonitoredItemsCore(server, nodeManager, error);
            if (detachOwner)
            {
                DetachRetiredMonitoredItemsCore(server, nodeManager);
            }
            return default;
        }

        private static void RetireMonitoredItemsCore(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            ServiceResult error)
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
                if (!tracker.HasMonitoredItems(nodeManager))
                {
                    continue;
                }
                if (subscription is not INodeManagerMonitoredItemRetirementTracker retirementTracker)
                {
                    throw new NotSupportedException(
                        "The configured subscription cannot retire NodeManager-owned monitored items.");
                }
                retirementTracker.RetireMonitoredItems(nodeManager, error);
            }
        }

        private static void DetachRetiredMonitoredItemsCore(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            foreach (ISubscription subscription in server.SubscriptionManager.GetSubscriptions())
            {
                if (subscription is INodeManagerMonitoredItemRetirementTracker retirementTracker)
                {
                    retirementTracker.DetachRetiredMonitoredItems(nodeManager);
                }
            }
        }

        private static void InvalidateContinuationPoints(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            foreach (ISession session in server.SessionManager.GetSessions())
            {
                session.InvalidateContinuationPoints(nodeManager);
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
            CancellationToken ct)
        {
            foreach (IEventMonitoredItem monitoredItem in server.EventManager.GetMonitoredItems())
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

        private static async ValueTask<Exception?> CleanupPreparedAsync(
            IServerInternal? server,
            IDynamicNodeManagerHost host,
            PreparedNodeManager prepared)
        {
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
            }

            if (unbindException is not null && rollbackException is not null)
            {
                return new AggregateException(
                    "NodeManager unbinding and structural rollback both failed.",
                    unbindException,
                    rollbackException);
            }
            return unbindException ?? rollbackException;
        }

        /// <summary>
        /// Schedules a background pass that disposes any shadow-retired generation whose
        /// monitored items have drained. Invoked by the host from an ownership-sensitive
        /// monitored item request (for example, the Delete that drains the last item), so
        /// the teardown must never run inline on the request path: it is dispatched to the
        /// thread pool with the request's execution context suppressed and coordinated
        /// through the lifecycle semaphore, exactly like an explicit lifecycle operation.
        /// If cleanup cannot complete now (for example, a lifecycle operation is already
        /// running), a later lifecycle operation or shutdown retries it.
        /// </summary>
        private void ScheduleRetiredGenerationDrainCleanup()
        {
            if (m_disposed || m_shuttingDown)
            {
                return;
            }
            lock (m_registrationLock)
            {
                if (m_retiredNodeManagers.Count == 0)
                {
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
                _ = Task.Run(DrainRetiredGenerationsAsync);
            }
            finally
            {
                if (restoreFlow)
                {
                    ExecutionContext.RestoreFlow();
                }
            }
        }

        private async Task DrainRetiredGenerationsAsync()
        {
            try
            {
                if (m_disposed || m_shuttingDown)
                {
                    return;
                }

                await m_lifecycleSemaphore
                    .WaitAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                try
                {
                    if (m_disposed || m_shuttingDown)
                    {
                        return;
                    }
                    if (!TryGetRunningServer(
                        out IServerInternal server,
                        out IDynamicNodeManagerHost host))
                    {
                        return;
                    }

                    await CleanupRetiredNodeManagersAsync(server, host)
                        .ConfigureAwait(false);
                }
                finally
                {
                    m_lifecycleSemaphore.Release();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Prompt cleanup is best-effort. Any generation that could not be torn down
                // here is retried by the next lifecycle operation or server shutdown.
            }
        }

        private bool TryGetRunningServer(
            out IServerInternal server,
            out IDynamicNodeManagerHost host)
        {
            server = null!;
            host = null!;
            if (m_disposed || m_shuttingDown)
            {
                return false;
            }
            if (m_server.CurrentState != ServerState.Running)
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
            IDynamicNodeManagerHost host)
        {
            RetiredNodeManager[] retired;
            lock (m_registrationLock)
            {
                retired = [.. m_retiredNodeManagers];
            }

            foreach (RetiredNodeManager retiredNodeManager in retired)
            {
                bool cleaned = await CleanupRetiredNodeManagerAsync(
                    server,
                    host,
                    retiredNodeManager).ConfigureAwait(false);
                if (cleaned)
                {
                    lock (m_registrationLock)
                    {
                        m_retiredNodeManagers.Remove(retiredNodeManager);
                    }
                }
            }
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
        private static async ValueTask<bool> CleanupRetiredNodeManagerAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            RetiredNodeManager retired)
        {
            if (retired.NeedsDetachment)
            {
                if (retired.AllowActiveMonitoredItems &&
                    HasActiveMonitoredItems(server, retired.NodeManager))
                {
                    return false;
                }

                if (retired.ImmediateRetirementError is not null)
                {
                    await RetireMonitoredItemsAsync(
                            server,
                            retired.NodeManager,
                            retired.ImmediateRetirementError)
                        .ConfigureAwait(false);
                }
                InvalidateContinuationPoints(server, retired.NodeManager);
                await server.RequestManager
                    .WaitForCurrentRequestsAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                InvalidateContinuationPoints(server, retired.NodeManager);
                if (retired.ImmediateRetirementError is not null)
                {
                    // A request that captured the old routing generation before the
                    // switch may have completed monitored-item creation during the
                    // drain. Retire that late item before detaching the owner.
                    await RetireMonitoredItemsAsync(
                            server,
                            retired.NodeManager,
                            retired.ImmediateRetirementError,
                            detachOwner: true)
                        .ConfigureAwait(false);
                }
                EnsureNoActiveMonitoredItems(server, retired.NodeManager);
                await UnbindFromServerAsync(
                    server,
                    retired.NodeManager,
                    CancellationToken.None).ConfigureAwait(false);
                retired.NeedsDetachment = false;
            }

            if (retired.PendingReferences.Count > 0)
            {
                await server.NodeManager
                    .RemoveReferencesAsync(
                        retired.PendingReferences,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                retired.PendingReferences.Clear();
            }

            await host
                .DestroyAsync(
                    retired.NodeManager,
                    removeExternalReferences: false,
                    ct: CancellationToken.None)
                .ConfigureAwait(false);
            RebuildActiveTypeTree(server);
            await DisposeNodeManagerAsync(retired.NodeManager).ConfigureAwait(false);
            return true;
        }

        private delegate ValueTask<IAsyncNodeManager> CreateNodeManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken ct);

        internal sealed class MonitoredItemTransition
        {
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

                    ServiceResult result = await m_current
                        .DetachMonitoredItemAsync(monitoredItem, ct)
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
                        ServiceResult result = await m_replacement
                            .AttachMonitoredItemAsync(monitoredItem, ct)
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
                        await m_replacement
                            .DetachMonitoredItemAsync(monitoredItem, ct)
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

            public async ValueTask RollbackAsync(CancellationToken ct)
            {
                var failures = new List<Exception>();
                if (m_replacement is not null)
                {
                    for (int ii = m_attachedItems.Count - 1; ii >= 0; ii--)
                    {
                        try
                        {
                            ServiceResult result = await m_replacement
                                .DetachMonitoredItemAsync(m_attachedItems[ii], ct)
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
                        ServiceResult result = await m_current
                            .RecoverMonitoredItemAsync(m_detachedItems[ii], ct)
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
                ServiceResult? immediateRetirementError = null)
            {
                NodeManager = nodeManager;
                PendingReferences = pendingReferences;
                NeedsDetachment = needsDetachment;
                AllowActiveMonitoredItems = allowActiveMonitoredItems;
                ImmediateRetirementError = immediateRetirementError;
            }

            public IAsyncNodeManager NodeManager { get; }

            public List<LocalReference> PendingReferences { get; }

            public bool NeedsDetachment { get; set; }

            /// <summary>
            /// Gets whether this generation was retired by a shadow reload and may still
            /// own active monitored items. Cleanup is deferred rather than rejected while
            /// this holds true and monitored items remain.
            /// </summary>
            public bool AllowActiveMonitoredItems { get; }

            /// <summary>
            /// Gets the status queued to monitored items before immediate cleanup.
            /// </summary>
            public ServiceResult? ImmediateRetirementError { get; }
        }

        private readonly StandardServer m_server;
        private readonly SemaphoreSlim m_lifecycleSemaphore = new(1, 1);
        private readonly Lock m_registrationLock = new();
        private readonly Dictionary<Guid, RegistrationState> m_registrations = [];
        private readonly List<RetiredNodeManager> m_retiredNodeManagers = [];
        private bool m_disposed;
        private volatile bool m_shuttingDown;
    }
}
