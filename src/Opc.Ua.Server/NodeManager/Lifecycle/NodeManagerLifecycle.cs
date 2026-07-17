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
                lock (m_registrationLock)
                {
                    registrations = [.. m_registrations.Values];
                    m_registrations.Clear();
                }

                var host =
                    server.NodeManager as IDynamicNodeManagerHost;
                foreach (RegistrationState registration in registrations)
                {
                    host?.Release(registration.Prepared.NodeManager);
                    await DisposeNodeManagerAsync(registration.Prepared.NodeManager)
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

            EnsureNotRequestCallback();
            await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                (IServerInternal server, IDynamicNodeManagerHost host) = GetRunningServer();
                await CleanupRetiredNodeManagersAsync(server, host).ConfigureAwait(false);
                RegistrationState state = GetCurrentState(registration);
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
                    EnsureNoActiveMonitoredItems(server, state.Prepared.NodeManager);
                    await host
                        .UnpublishAsync(state.Prepared.NodeManager, ct)
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
                        EnsureNoActiveMonitoredItems(
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
                                CancellationToken.None).ConfigureAwait(false);
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
            CancellationToken ct)
        {
            EnsureNotRequestCallback();
            await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
            IAsyncNodeManager? nodeManager = null;
            PreparedNodeManager? prepared = null;
            IServerInternal? server = null;
            IDynamicNodeManagerHost? host = null;
            int namespaceCountBefore = 0;
            bool committed = false;
            try
            {
                (server, host) = GetRunningServer();
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
                    ct).ConfigureAwait(false);

                var registration = new NodeManagerRegistration(
                    Guid.NewGuid(),
                    1,
                    nodeManager);
                lock (m_registrationLock)
                {
                    m_registrations.Add(
                        registration.Id,
                        new RegistrationState(registration, prepared));
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
                                prepared);
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
            CancellationToken ct)
        {
            if (registration is null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            EnsureNotRequestCallback();
            await m_lifecycleSemaphore.WaitAsync(ct).ConfigureAwait(false);
            IAsyncNodeManager? replacementManager = null;
            PreparedNodeManager? replacement = null;
            RegistrationState? current = null;
            List<LocalReference> droppedInboundReferences = [];
            IServerInternal? server = null;
            IDynamicNodeManagerHost? host = null;
            int namespaceCountBefore = 0;
            try
            {
                (server, host) = GetRunningServer();
                await CleanupRetiredNodeManagersAsync(server, host).ConfigureAwait(false);
                namespaceCountBefore = server.NamespaceUris.Count;
                current = GetCurrentState(registration);
                EnsureNoActiveMonitoredItems(server, current.Prepared.NodeManager);

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
                    .ReplaceAsync(current.Prepared.NodeManager, replacement, ct)
                    .ConfigureAwait(false);
                await CommitWithReconciliationAsync(
                    server,
                    host,
                    replacement,
                    replacementManager,
                    bindings,
                    ct).ConfigureAwait(false);
                current.Prepared.Published = false;
                var nextRegistration = new NodeManagerRegistration(
                    current.Registration.Id,
                    current.Registration.Generation + 1,
                    replacement.NodeManager);
                lock (m_registrationLock)
                {
                    m_registrations[current.Registration.Id] = new RegistrationState(
                        nextRegistration,
                        replacement);
                }

                var retired = new RetiredNodeManager(
                    current.Prepared.NodeManager,
                    droppedInboundReferences,
                    needsDetachment: true);
                lock (m_registrationLock)
                {
                    m_retiredNodeManagers.Add(retired);
                }
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
                    await CleanupRetiredNodeManagerAsync(server, host, retired)
                        .ConfigureAwait(false);
                    lock (m_registrationLock)
                    {
                        m_retiredNodeManagers.Remove(retired);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    throw new InvalidOperationException(
                        "The replacement NodeManager is live, but the retired generation " +
                        "could not be cleaned up. A later lifecycle operation will retry cleanup.",
                        ex);
                }

                await NotifyCommittedChangeAsync(
                    server,
                    "reloaded",
                    namespaceCountBefore,
                    CancellationToken.None,
                    semanticChanges).ConfigureAwait(false);
                return nextRegistration;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
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
                        registrationAlreadyUpdated =
                            m_registrations.TryGetValue(
                                current.Registration.Id,
                                out RegistrationState? retainedState) &&
                            ReferenceEquals(
                                retainedState.Registration.NodeManager,
                                replacementManager);
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
                                    replacement);
                            m_retiredNodeManagers.Add(
                                new RetiredNodeManager(
                                    current.Prepared.NodeManager,
                                    droppedInboundReferences,
                                    needsDetachment: true));
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
                    throw new InvalidOperationException(
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

        private (IServerInternal Server, IDynamicNodeManagerHost Host) GetRunningServer()
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
            if (server.RequestManager.IsExecutingRequest)
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

        private void EnsureNotRequestCallback()
        {
            if (m_server.CurrentState == ServerState.Running &&
                m_server.CurrentInstance.RequestManager.IsExecutingRequest)
            {
                throw new InvalidOperationException(
                    "NodeManager lifecycle operations cannot run from an OPC UA request callback.");
            }
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

        private static void EnsureNoActiveMonitoredItems(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            foreach (ISubscription subscription in server.SubscriptionManager.GetSubscriptions())
            {
                if (subscription.MonitoredItemCount == 0)
                {
                    continue;
                }
                if (subscription is not INodeManagerMonitoredItemTracker tracker)
                {
                    throw new NotSupportedException(
                        "The configured subscription cannot verify NodeManager ownership.");
                }
                if (tracker.HasMonitoredItems(nodeManager))
                {
                    throw new InvalidOperationException(
                        "The NodeManager cannot be reloaded or removed while it owns monitored items.");
                }
            }
        }

        private static void InvalidateContinuationPoints(
            IServerInternal server,
            IAsyncNodeManager nodeManager)
        {
            foreach (ISession session in server.SessionManager.GetSessions())
            {
                if (session is INodeManagerContinuationPointTracker tracker)
                {
                    tracker.InvalidateContinuationPoints(nodeManager);
                }
            }
        }

        private static async ValueTask CommitWithReconciliationAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            PreparedNodeManager prepared,
            IAsyncNodeManager nodeManager,
            ServerBindings bindings,
            CancellationToken ct)
        {
            await host.CommitAsync(
                prepared,
                () => ReconcileBindingsAsync(
                    server,
                    nodeManager,
                    bindings,
                    ct),
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
                        IsSessionClosing(server, session))
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
                        !IsSessionClosing(server, session))
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

                await nodeManager
                    .SessionClosingAsync(
                        new OperationContext(
                            binding.Value.Session,
                            DiagnosticsMasks.None),
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

                await nodeManager
                    .SubscribeToAllEventsAsync(
                        new OperationContext(binding.Value),
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

                await nodeManager
                    .SubscribeToAllEventsAsync(
                        new OperationContext(monitoredItem),
                        monitoredItem.SubscriptionId,
                        monitoredItem,
                        true,
                        ct)
                    .ConfigureAwait(false);
            }

            foreach (ISession session in server.SessionManager.GetSessions())
            {
                var context = new OperationContext(session, DiagnosticsMasks.None);
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
                await nodeManager
                    .SubscribeToAllEventsAsync(
                        new OperationContext(monitoredItem),
                        monitoredItem.SubscriptionId,
                        monitoredItem,
                        true,
                        ct)
                    .ConfigureAwait(false);
            }

            foreach (KeyValuePair<NodeId, SessionBinding> binding in
                bindings.Sessions)
            {
                await nodeManager
                    .SessionClosingAsync(
                        new OperationContext(
                            binding.Value.Session,
                            DiagnosticsMasks.None),
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
                var context = new OperationContext(session, DiagnosticsMasks.None);
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
                    !IsSessionClosing(server, session) &&
                    server.SessionManager.GetSessions().Any(current =>
                        ReferenceEquals(current, session)))
                {
                    return new SessionBinding(session, identity);
                }
                if (!session.Activated ||
                    IsSessionClosing(server, session) ||
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

        private static bool IsSessionClosing(
            IServerInternal server,
            ISession session)
        {
            return server is ISessionClosingRegistry registry &&
                registry.IsSessionClosing(session.Id);
        }

        private static async ValueTask<bool> SubscribeToAllEventsAsync(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            IEventMonitoredItem monitoredItem,
            CancellationToken ct)
        {
            var context = new OperationContext(monitoredItem);
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

            await nodeManager
                .SubscribeToAllEventsAsync(
                    new OperationContext(monitoredItem),
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
                if (nodeManager is AsyncCustomNodeManager asyncCustomNodeManager)
                {
                    asyncCustomNodeManager.RebuildTypeTree();
                }
                else if (nodeManager.SyncNodeManager is
                    CustomNodeManager2 customNodeManager)
                {
                    customNodeManager.RebuildTypeTree();
                }
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
                await CleanupRetiredNodeManagerAsync(
                    server,
                    host,
                    retiredNodeManager).ConfigureAwait(false);
                lock (m_registrationLock)
                {
                    m_retiredNodeManagers.Remove(retiredNodeManager);
                }
            }
        }

        private static async ValueTask CleanupRetiredNodeManagerAsync(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            RetiredNodeManager retired)
        {
            if (retired.NeedsDetachment)
            {
                InvalidateContinuationPoints(server, retired.NodeManager);
                await server.RequestManager
                    .WaitForCurrentRequestsAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                InvalidateContinuationPoints(server, retired.NodeManager);
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
        }

        private delegate ValueTask<IAsyncNodeManager> CreateNodeManagerAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken ct);

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
                bool needsDetachment)
            {
                NodeManager = nodeManager;
                PendingReferences = pendingReferences;
                NeedsDetachment = needsDetachment;
            }

            public IAsyncNodeManager NodeManager { get; }

            public List<LocalReference> PendingReferences { get; }

            public bool NeedsDetachment { get; set; }
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
