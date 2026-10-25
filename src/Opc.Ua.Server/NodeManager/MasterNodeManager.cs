/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <inheritdoc/>
    public partial class MasterNodeManager :
        IDisposable,
        IAsyncDisposable,
        IMasterNodeManager,
        IMonitoredItemTransferCoordinator,
        IDynamicNodeManagerHost
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        /// <param name="server">The server hosting the NodeManagers.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="dynamicNamespaceUri">The dynamic NodeManager namespace URI, or <c>null</c>.</param>
        /// <param name="additionalManagers">The synchronous NodeManagers to add.</param>
        public MasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string? dynamicNamespaceUri,
            params INodeManager[] additionalManagers)
            : this(server, configuration, dynamicNamespaceUri, null, additionalManagers)
        {
        }

        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        /// <param name="server">The server hosting the NodeManagers.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="dynamicNamespaceUri">The dynamic NodeManager namespace URI, or <c>null</c>.</param>
        /// <param name="additionalManagers">The asynchronous NodeManagers to add.</param>
        public MasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string? dynamicNamespaceUri,
            params IAsyncNodeManager[] additionalManagers)
            : this(server, configuration, dynamicNamespaceUri, additionalManagers, null)
        {
        }

        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        /// <param name="server">The server hosting the NodeManagers.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="dynamicNamespaceUri">The dynamic NodeManager namespace URI, or <c>null</c>.</param>
        /// <param name="additionalManagers">The asynchronous NodeManagers to add, or <c>null</c>.</param>
        /// <param name="additionalSyncManagers">The synchronous NodeManagers to add, or <c>null</c>.</param>
        public MasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string? dynamicNamespaceUri,
            IEnumerable<IAsyncNodeManager>? additionalManagers,
            IEnumerable<INodeManager>? additionalSyncManagers)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            Server = server ?? throw new ArgumentNullException(nameof(server));
            m_logger = server.Telemetry.CreateLogger<MasterNodeManager>();
            GenerationReconciler = new GenerationReconciler(server);

            m_nodeManagers = [];
            m_maxContinuationPointsPerBrowse = (uint)configuration.ServerConfiguration!
                .MaxBrowseContinuationPoints;

            // ensure the dynamic namespace uris.
            int dynamicNamespaceIndex = 1;

            if (!string.IsNullOrEmpty(dynamicNamespaceUri))
            {
                dynamicNamespaceIndex = server.NamespaceUris.GetIndex(dynamicNamespaceUri!);

                if (dynamicNamespaceIndex == -1)
                {
                    dynamicNamespaceIndex = server.NamespaceUris.Append(dynamicNamespaceUri!);
                }
            }

            // need to build a table of NamespaceIndexes and their NodeManagers.
            List<IAsyncNodeManager> registeredManagers;
            var namespaceManagers = new Dictionary<int, List<IAsyncNodeManager>>
            {
                [0] = [],
                [1] = registeredManagers = []
            };

            // always add the diagnostics and configuration node manager to the start of the list.
            IConfigurationNodeManager configurationAndDiagnosticsManager
                = server.MainNodeManagerFactory.CreateConfigurationNodeManager();

            RegisterNodeManager(
                configurationAndDiagnosticsManager,
                registeredManagers,
                namespaceManagers);

            // add the core node manager second because the diagnostics node manager takes priority.
            // always add the core node manager to the second of the list.
            ICoreNodeManager coreNodeManager = server.MainNodeManagerFactory.CreateCoreNodeManager((ushort)dynamicNamespaceIndex);

            m_nodeManagers.AddInitial(coreNodeManager);

            // register core node manager for default UA namespace.
            namespaceManagers[0].Add(m_nodeManagers[1]);

            // register core node manager for built-in server namespace.
            namespaceManagers[1].Add(m_nodeManagers[1]);

            // add the custom NodeManagers provided by the application.
            if (additionalManagers != null)
            {
                foreach (IAsyncNodeManager nodeManager in additionalManagers)
                {
                    RegisterNodeManager(nodeManager, registeredManagers, namespaceManagers);
                    m_startupApplicationNodeManagers.Add(
                        new StartupNodeManagerState(nodeManager));
                }
            }
            if (additionalSyncManagers != null)
            {
                foreach (INodeManager nodeManager in additionalSyncManagers)
                {
                    IAsyncNodeManager asyncNodeManager = nodeManager.ToAsyncNodeManager();
                    RegisterNodeManager(
                        asyncNodeManager,
                        registeredManagers,
                        namespaceManagers);
                    m_startupApplicationNodeManagers.Add(
                        new StartupNodeManagerState(asyncNodeManager));
                }
            }

            // Publish the initial manager and namespace routing snapshot.
            var factory = server.Factory as EncodeableFactory;
            m_nodeManagers.Initialize(
                namespaceManagers,
                server.TypeTree,
                factory);

            m_serviceDispatch = new NodeManagerServiceDispatcher(this, m_nodeManagers, server, m_logger);
        }

        /// <summary>
        /// Registers the node manager with the master node manager.
        /// </summary>
        private void RegisterNodeManager(
            IAsyncNodeManager nodeManager,
            List<IAsyncNodeManager> registeredManagers,
            Dictionary<int, List<IAsyncNodeManager>> namespaceManagers)
        {
            m_nodeManagers.AddInitial(nodeManager);

            // ensure the NamespaceUris supported by the NodeManager are in the Server's NamespaceTable.
            if (nodeManager.NamespaceUris != null)
            {
                foreach (string namespaceUri in nodeManager.NamespaceUris)
                {
                    // look up the namespace uri.
                    int index = Server.NamespaceUris.GetIndex(namespaceUri);

                    if (index == -1)
                    {
                        index = Server.NamespaceUris.Append(namespaceUri);
                    }

                    // add manager to list for the namespace.
                    if (!namespaceManagers.TryGetValue(index, out registeredManagers!))
                    {
                        namespaceManagers[index] = registeredManagers = [];
                    }

                    registeredManagers.Add(nodeManager);
                }
            }
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }
            lock (m_disposalLock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_disposalTask = DisposeNodeManagersAsync();
            }
        }

        /// <summary>
        /// Disposes the owned node managers and waits for their admitted operations to drain.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            Dispose();
            Task disposal;
            lock (m_disposalLock)
            {
                disposal = m_disposalTask;
            }
            await disposal.ConfigureAwait(false);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Drains and disposes owned node managers, collecting failures before releasing lifecycle resources.
        /// </summary>
        private async Task DisposeNodeManagersAsync()
        {
            await PrepareNodeManagersForShutdownAsync().ConfigureAwait(false);
            await m_startupShutdownSemaphoreSlim.WaitAsync().ConfigureAwait(false);
            var errors = new List<Exception>();
            try
            {
                List<IAsyncNodeManager> nodeManagers = [.. m_nodeManagers];
                m_nodeManagers.Clear();
                m_dynamicExternalReferences.Clear();

                foreach (IAsyncNodeManager nodeManager in nodeManagers)
                {
                    try
                    {
                        ChangePreparer.Detach(nodeManager);
                        if (nodeManager is IAsyncDisposable asyncDisposable)
                        {
                            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            (nodeManager as IDisposable)?.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        m_logger.NodeManagerDeferredCleanupFailed(ex);
                        errors.Add(ex);
                    }
                }
            }
            finally
            {
                m_startupShutdownSemaphoreSlim.Release();
                m_startupShutdownSemaphoreSlim.Dispose();
                m_dynamicMutationSemaphore.Dispose();
            }
            if (errors.Count > 0)
            {
                throw new AggregateException("Node-manager disposal failed.", errors);
            }
        }

        /// <summary>
        /// Adds a reference to the table of external references.
        /// </summary>
        /// <remarks>
        /// This is a convenience function used by custom NodeManagers.
        /// </remarks>
        public static void CreateExternalReference(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            NodeId targetId)
        {
            var reference = new ReferenceNode
            {
                ReferenceTypeId = referenceTypeId,
                IsInverse = isInverse,
                TargetId = targetId
            };

            if (!externalReferences.TryGetValue(sourceId, out IList<IReference>? references))
            {
                externalReferences[sourceId] = references = [];
            }

            references!.Add(reference);
        }

        /// <inheritdoc/>
        public ICoreNodeManager? CoreNodeManager => m_nodeManagers[1] as ICoreNodeManager;

        /// <inheritdoc/>
        public IDiagnosticsNodeManager? DiagnosticsNodeManager
            => m_nodeManagers[0] as IDiagnosticsNodeManager;

        /// <inheritdoc/>
        public IConfigurationNodeManager? ConfigurationNodeManager
            => m_nodeManagers[0] as IConfigurationNodeManager;

        /// <inheritdoc/>
        public virtual async ValueTask StartupAsync(CancellationToken cancellationToken = default)
        {
            await m_startupShutdownSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                m_shutdownCompletedNodeManagers.Clear();
                Volatile.Write(ref m_shutdownCompletedNodeManagerCount, 0);
                m_logger.MasterNodeManagerStartupNodeManagersCount(m_nodeManagers.Count);

                // create the address spaces.
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();

                foreach (IAsyncNodeManager nodeManager in m_nodeManagers)
                {
                    StartupNodeManagerState? startupState =
                        FindStartupApplicationNodeManager(nodeManager);
                    Dictionary<NodeId, Dictionary<IReference, int>>? referencesBefore =
                        startupState is null
                            ? null
                            : SnapshotExternalReferences(externalReferences);
                    try
                    {
                        await nodeManager.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                            .ConfigureAwait(false);
                        if (startupState is not null)
                        {
                            startupState.ExternalReferences =
                                CaptureAddedExternalReferences(
                                    referencesBefore!,
                                    externalReferences);
                        }
                    }
                    catch (Exception e)
                    {
                        m_logger.UnexpectedErrorCreatingAddressSpaceForNodeManager(e, nodeManager.GetType().Name);
                        throw;
                    }
                }

                foreach (IAsyncNodeManager nodeManager in m_nodeManagers)
                {
                    if (nodeManager is AsyncCustomNodeManager customNodeManager)
                    {
                        customNodeManager.ReconcileHistoricalAccessAdvertisement();
                    }
                }

                // update external references.
                foreach (IAsyncNodeManager nodeManager in m_nodeManagers)
                {
                    try
                    {
                        await nodeManager.AddReferencesAsync(externalReferences, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        m_logger.UnexpectedErrorAddingReferencesForNodeManagerNodeManager(e, nodeManager.GetType().Name);
                        throw;
                    }
                }

                // Retain them: a NodeManager added later may own a Node that one
                // of these references targets, and a reference to a Node that is
                // not yet in the address space is dropped rather than queued.
                // Startup gets away with collecting first and applying second, so
                // order does not matter here; the dynamic path has no such second
                // phase and replays these instead.
                m_startupExternalReferences = externalReferences;
            }
            finally
            {
                m_startupShutdownSemaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask SessionClosingAsync(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions,
            CancellationToken cancellationToken = default)
        {
            await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            m_dynamicMutationSemaphore.Release();

            await m_startupShutdownSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                IAsyncNodeManager[] activeNodeManagers = [.. m_nodeManagers];
                NotificationDispatchLease[] dispatches =
                    GetSessionNotificationDispatches(activeNodeManagers);
                try
                {
                    foreach (NotificationDispatchLease dispatch in dispatches)
                    {
                        try
                        {
                            bool closed = dispatch.Generation.Gate.State == NodeManagerGenerationState.Live &&
                                await GenerationReconciler.CloseSessionAsync(
                                    dispatch.Generation,
                                    sessionId,
                                    cancellationToken,
                                    deleteSubscriptions).ConfigureAwait(false);
                            if (!closed)
                            {
                                await dispatch.NodeManager.SessionClosingAsync(
                                    context,
                                    sessionId,
                                    deleteSubscriptions,
                                    cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (Exception e)
                        {
                            m_logger.UnexpectedErrorClosingSessionForNodeManagerNodeManager(
                                e,
                                dispatch.NodeManager.GetType().Name);
                        }
                    }
                }
                finally
                {
                    DisposeNotificationDispatches(dispatches);
                }
            }
            finally
            {
                m_startupShutdownSemaphoreSlim.Release();
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask SessionActivatedAsync(
            OperationContext context,
            NodeId sessionId,
            CancellationToken cancellationToken = default)
        {
            IAsyncNodeManager[] activeNodeManagers = [.. m_nodeManagers];
            NotificationDispatchLease[] dispatches =
                GetSessionNotificationDispatches(activeNodeManagers);
            ISession? activatedSession = Server.SessionManager.GetSessions().FirstOrDefault(
                current => current.Id == sessionId);
            try
            {
                foreach (NotificationDispatchLease dispatch in dispatches)
                {
                    try
                    {
                        if (dispatch.Generation.Gate.State == NodeManagerGenerationState.Live &&
                            activatedSession is not null)
                        {
                            await GenerationReconciler.ActivateSessionAsync(
                                dispatch.Generation,
                                activatedSession,
                                cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await dispatch.NodeManager.SessionActivatedAsync(
                                context,
                                sessionId,
                                cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception e)
                    {
                        m_logger.UnexpectedErrorNotifyingNodeManagerOfSession(
                            e,
                            dispatch.NodeManager.GetType().Name);
                    }
                }
            }
            finally
            {
                DisposeNotificationDispatches(dispatches);
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PrepareNodeManagersForShutdownAsync().ConfigureAwait(false);
            await m_startupShutdownSemaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                IAsyncNodeManager[] nodeManagers = [.. m_nodeManagers];
                m_logger.MasterNodeManagerShutdownNodeManagersCount(nodeManagers.Length);
                var failures = new List<Exception>();
                OperationCanceledException? cancellationException = null;

                foreach (IAsyncNodeManager nodeManager in nodeManagers)
                {
                    if (m_shutdownCompletedNodeManagers.Contains(nodeManager))
                    {
                        continue;
                    }

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ChangePreparer.Detach(nodeManager);
                        await nodeManager.DeleteAddressSpaceAsync(cancellationToken)
                            .ConfigureAwait(false);
                        m_shutdownCompletedNodeManagers.Add(nodeManager);
                        Interlocked.Increment(ref m_shutdownCompletedNodeManagerCount);
                    }
                    catch (OperationCanceledException ex) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        cancellationException = ex;
                        break;
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        failures.Add(
                            new InvalidOperationException(
                                $"NodeManager '{nodeManager.GetType().Name}' failed to delete its address space during shutdown.",
                                ex));
                    }
                }

                if (cancellationException is not null)
                {
                    throw cancellationException;
                }
                if (failures.Count > 0)
                {
                    throw new AggregateException(
                        "One or more NodeManagers failed to delete their address spaces during shutdown.",
                        failures);
                }
            }
            finally
            {
                m_startupShutdownSemaphoreSlim.Release();
            }
        }

        /// <summary>
        /// Lets participating node managers drain accepted work before serialized address-space teardown.
        /// </summary>
        private async ValueTask PrepareNodeManagersForShutdownAsync()
        {
            foreach (IAsyncNodeManager nodeManager in m_nodeManagers)
            {
                if (nodeManager is INodeManagerShutdown shutdown)
                {
                    await shutdown.PrepareForShutdownAsync().ConfigureAwait(false);
                }
            }
        }

        async ValueTask<PreparedNodeManager> IDynamicNodeManagerHost.PrepareAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            ct.ThrowIfCancellationRequested();
            bool prepared = false;
            bool preparationStarted = false;
            try
            {
                if (m_nodeManagers.Contains(nodeManager))
                {
                    throw new NodeManagerAlreadyRegisteredException();
                }
                preparationStarted = true;
                SetPreparing(nodeManager, preparing: true);
                SetExistingEventSubscriptionSuppression(nodeManager, suppress: true);
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await nodeManager
                    .CreateAddressSpaceAsync(externalReferences, ct)
                    .ConfigureAwait(false);
                ChangePreparer.GetServer(nodeManager)?.AppendRootNotifierReferences(externalReferences);
                ChangePreparer.GetServer(nodeManager)?.CaptureExternalReferences(externalReferences);
                prepared = true;
                return new PreparedNodeManager(nodeManager, externalReferences);
            }
            catch (Exception ex) when (
                ex is not OutOfMemoryException and
                    not NodeManagerAlreadyRegisteredException)
            {
                if (ChangePreparer.GetServer(nodeManager)?.IsStaging != true)
                {
                    m_nodeManagers.RemoveNamespaceManager(nodeManager);
                }
                try
                {
                    ChangePreparer.Detach(nodeManager);
                    await nodeManager
                        .DeleteAddressSpaceAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception cleanupException) when (
                    cleanupException is not OutOfMemoryException)
                {
                    throw new AggregateException(
                        "NodeManager preparation and cleanup both failed.",
                        ex,
                        cleanupException);
                }
                throw;
            }
            finally
            {
                if (preparationStarted && !prepared)
                {
                    SetPreparing(nodeManager, preparing: false);
                }
                if (preparationStarted)
                {
                    SetExistingEventSubscriptionSuppression(
                        nodeManager,
                        suppress: false);
                }
            }
        }

        /// <inheritdoc/>
        async ValueTask IDynamicNodeManagerHost.DestroyAddressSpaceAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            await FinalizeRetiredGenerationNotificationsAsync(nodeManager, ct)
                .ConfigureAwait(false);
            if (nodeManager is INodeManagerShutdown shutdown)
            {
                await shutdown.PrepareForShutdownAsync().ConfigureAwait(false);
            }
            // The lifecycle owns this detached generation. Its deletion may remove
            // dependent NodeManagers, so it must not serialize against their preparation.
            ChangePreparer.Detach(nodeManager);
            await nodeManager.DeleteAddressSpaceAsync(ct).ConfigureAwait(false);
        }

        async ValueTask IDynamicNodeManagerHost.RemoveDestroyedExternalReferencesAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            // Reclamation exclusively owns this detached manager's removed-reference list.
            // Reference callbacks may publish dependencies, so no publication semaphore may be held.
            List<LocalReference> referencesToRemove = nodeManager switch
            {
                AsyncCustomNodeManager asyncCustomNodeManager =>
                    asyncCustomNodeManager.GetRemovedExternalReferences(),
                _ when nodeManager.SyncNodeManager is
                    CustomNodeManager2 customNodeManager =>
                    customNodeManager.GetRemovedExternalReferences(),
                _ => []
            };
            // Publication already reconciled these exact contributions. Replaying their removal could erase a
            // replacement's same-identifier reference or invalidate an independent transaction's preparation.
            ChangePreparer.GetServer(nodeManager)?.ExcludePreparedReferences(referencesToRemove);
            if (referencesToRemove.Count > 0)
            {
                await RemoveReferencesAsync(referencesToRemove, ct).ConfigureAwait(false);
            }
            if (nodeManager is AsyncCustomNodeManager asyncManagerToClear)
            {
                asyncManagerToClear.ClearRemovedExternalReferences();
            }
            else if (nodeManager.SyncNodeManager is
                CustomNodeManager2 syncManagerToClear)
            {
                syncManagerToClear.ClearRemovedExternalReferences();
            }
        }

        async ValueTask IDynamicNodeManagerHost.AbortAsync(
            PreparedNodeManager prepared,
            CancellationToken ct)
        {
            ValidatePreparedNodeManager(prepared);
            if (m_nodeManagers.Contains(prepared.NodeManager))
            {
                throw new InvalidOperationException("A serving NodeManager cannot be aborted.");
            }
            if (ChangePreparer.GetServer(prepared.NodeManager)?.IsStaging != true)
            {
                m_nodeManagers.RemoveNamespaceManager(prepared.NodeManager);
            }
            SetPreparing(prepared.NodeManager, preparing: false);

            try
            {
                await ((IDynamicNodeManagerHost)this)
                    .DestroyAddressSpaceAsync(
                        prepared.NodeManager,
                        ct: ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (prepared.Generation is { Gate.State: NodeManagerGenerationState.Prepared } generation)
                {
                    generation.Gate.Abort();
                }
                ((IDynamicNodeManagerHost)this).Release(
                    prepared.NodeManager);
            }
        }

        async ValueTask<ArrayOf<PreparedNodeManager>>
            IDynamicNodeManagerHost.TakeStartupNodeManagersAsync(
                CancellationToken ct)
        {
            await m_startupShutdownSemaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (m_startupApplicationNodeManagersTransferred)
                {
                    throw new InvalidOperationException(
                        "Startup NodeManager ownership has already been transferred.");
                }
                if (m_startupExternalReferences is null)
                {
                    throw new InvalidOperationException(
                        "The startup address space has not been created.");
                }

                foreach (StartupNodeManagerState state in m_startupApplicationNodeManagers)
                {
                    if (state.ExternalReferences is null)
                    {
                        throw new InvalidOperationException(
                            "Startup NodeManager external-reference ownership was not captured.");
                    }
                    if (m_dynamicExternalReferences.ContainsKey(state.NodeManager))
                    {
                        throw new InvalidOperationException(
                            "A startup NodeManager is already owned by the live lifecycle provider.");
                    }
                }

                Dictionary<NodeId, IList<IReference>> retainedStartupReferences =
                    CloneExternalReferences(m_startupExternalReferences);
                var prepared =
                    new PreparedNodeManager[m_startupApplicationNodeManagers.Count];

                for (int ii = 0; ii < m_startupApplicationNodeManagers.Count; ii++)
                {
                    StartupNodeManagerState state = m_startupApplicationNodeManagers[ii];
                    Dictionary<NodeId, IList<IReference>> externalReferences =
                        state.ExternalReferences!;
                    RemoveExternalReferences(
                        retainedStartupReferences,
                        externalReferences);
                    prepared[ii] = new PreparedNodeManager(
                        state.NodeManager,
                        externalReferences)
                    {
                        Generation = m_nodeManagers.GetGeneration(state.NodeManager),
                        Published = true
                    };
                }

                for (int ii = 0; ii < prepared.Length; ii++)
                {
                    PreparedNodeManager nodeManager = prepared[ii];
                    m_dynamicExternalReferences.Add(
                        nodeManager.NodeManager,
                        nodeManager.ExternalReferences);
                }

                m_startupExternalReferences = retainedStartupReferences;
                m_startupApplicationNodeManagers.Clear();
                m_startupApplicationNodeManagersTransferred = true;
                return new ArrayOf<PreparedNodeManager>(prepared);
            }
            finally
            {
                m_startupShutdownSemaphoreSlim.Release();
            }
        }

        void IDynamicNodeManagerHost.Release(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            if (m_dynamicExternalReferences.Remove(nodeManager))
            {
                m_nodeManagers.Remove(nodeManager);
            }
        }

        void IDynamicNodeManagerHost.SetRetiredGenerationDrainObserver(Action? observer)
        {
            m_retiredGenerationDrainObserver = observer;
        }

        /// <summary>
        /// Signals the lifecycle drain observer that a retired generation may have released
        /// its last monitored item. Service dispatch calls this instead of touching the
        /// lifecycle-owned observer field directly.
        /// </summary>
        internal void NotifyRetiredGenerationDrainObserver()
        {
            m_retiredGenerationDrainObserver?.Invoke();
        }

        void IDynamicNodeManagerHost.CloseNodeManagerGenerationAdmissions(
            IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            NodeManagerGenerationRegistry.GetOrCreate(nodeManager).Gate.Retire();
        }

        async ValueTask IDynamicNodeManagerHost
            .FinalizeRetiredGenerationNotificationsAsync(
                IAsyncNodeManager nodeManager,
                CancellationToken ct)
        {
            await FinalizeRetiredGenerationNotificationsAsync(nodeManager, ct)
                .ConfigureAwait(false);
        }

        private async ValueTask FinalizeRetiredGenerationNotificationsAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            NodeManagerGeneration generation = NodeManagerGenerationRegistry.GetOrCreate(nodeManager);
            if (generation.Gate.State == NodeManagerGenerationState.Prepared)
            {
                generation.Gate.Abort();
            }
            else
            {
                generation.Gate.Retire();
            }
            await generation.Gate.WaitForDrainAsync().WaitAsync(ct).ConfigureAwait(false);
            await GenerationReconciler.ReleaseRetiredEventsAsync(generation, ct).ConfigureAwait(false);
        }

        private NotificationDispatchLease[] GetSessionNotificationDispatches(
            IReadOnlyList<IAsyncNodeManager> activeNodeManagers)
        {
            return GetActiveNotificationDispatches(activeNodeManagers, []);
        }

        /// <summary>
        /// Acquires generation admissions for all-events callbacks on currently serving managers only.
        /// </summary>
        /// <param name="activeNodeManagers">The managers captured by the operation's routing view.</param>
        internal NotificationDispatchLease[] GetAllEventNotificationDispatches(
            IReadOnlyList<IAsyncNodeManager> activeNodeManagers)
        {
            return GetActiveNotificationDispatches(activeNodeManagers, []);
        }

        /// <summary>
        /// Acquires generation admissions for condition refresh on currently serving managers only.
        /// </summary>
        /// <param name="activeNodeManagers">The managers captured by the operation's routing view.</param>
        /// <param name="monitoredItems">The monitored items included in the operation.</param>
        internal NotificationDispatchLease[] GetConditionRefreshDispatches(
            IReadOnlyList<IAsyncNodeManager> activeNodeManagers,
            IList<IEventMonitoredItem> monitoredItems)
        {
            return GetActiveNotificationDispatches(activeNodeManagers, [.. monitoredItems]);
        }

        /// <summary>
        /// Acquires generation admissions for all-events unsubscribe on currently serving managers.
        /// Retiring generations are handled separately through their exact event binding records.
        /// </summary>
        internal NotificationDispatchLease[] GetAllEventUnsubscribeDispatches()
        {
            return GetActiveNotificationDispatches([.. m_nodeManagers], []);
        }

        private NotificationDispatchLease[] GetActiveNotificationDispatches(
            IReadOnlyList<IAsyncNodeManager> activeNodeManagers,
            IEventMonitoredItem[] monitoredItems)
        {
            var dispatches = new List<NotificationDispatchLease>(activeNodeManagers.Count);
            NodeManagerRoutingTable.RoutingSnapshot revision = m_nodeManagers.Revision;
            try
            {
                foreach (IAsyncNodeManager nodeManager in activeNodeManagers)
                {
                    if (revision.TryGetGeneration(nodeManager, out NodeManagerGeneration? generation) &&
                        generation!.Gate.TryEnter(out int slot))
                    {
                        var dispatch = new NotificationDispatchLease(this, generation, monitoredItems, slot);
                        try
                        {
                            dispatches.Add(dispatch);
                        }
                        catch
                        {
                            dispatch.Dispose();
                            throw;
                        }
                    }
                }
                return [.. dispatches];
            }
            catch
            {
                DisposeNotificationDispatches(dispatches);
                throw;
            }
        }

        /// <summary>
        /// Releases all notification dispatch leases in the supplied collection.
        /// </summary>
        /// <param name="dispatches">The notification dispatch leases associated with the operation.</param>
        internal static void DisposeNotificationDispatches(
            IReadOnlyList<NotificationDispatchLease> dispatches)
        {
            foreach (NotificationDispatchLease dispatch in dispatches)
            {
                dispatch.Dispose();
            }
        }

        /// <inheritdoc/>
        async ValueTask IDynamicNodeManagerHost.RecoverDetachedMonitoredItemsAsync(
            IAsyncNodeManager nodeManager,
            IReadOnlyCollection<NodeId>? nodeIds,
            CancellationToken cancellationToken)
        {
            if (!Server.IsRunning || GetVisibleNodeManager(nodeManager) is not { } visibleNodeManager)
            {
                return;
            }

            await RecoverMonitoredItemsAsync(
                visibleNodeManager,
                GetUnresolvedMonitoredItems(visibleNodeManager, nodeIds),
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Selects only unresolved items owned by the target's stable slot.
        /// </summary>
        /// <param name="nodeManager">The manager whose slot owns the unresolved items.</param>
        /// <param name="nodeIds">The nodes being added, or null to check every unresolved item.</param>
        /// <returns>A stable item snapshot, without inspecting unrelated subscriptions.</returns>
        private static IMonitoredItem[] GetUnresolvedMonitoredItems(
            IAsyncNodeManager nodeManager,
            IReadOnlyCollection<NodeId>? nodeIds)
        {
            NodeManagerSlot slot = NodeManagerGenerationRegistry.GetOrCreate(nodeManager).Slot;
            if (slot.UnresolvedItems.IsEmpty || nodeIds is { Count: 0 })
            {
                return [];
            }
            if (nodeIds is null)
            {
                return [.. slot.UnresolvedItems.Keys];
            }
            var requestedNodes = new HashSet<NodeId>(nodeIds, NodeIdComparer.Default);
            return [.. slot.UnresolvedItems.Keys.Where(item => requestedNodes.Contains(item.NodeId))];
        }

        /// <summary>
        /// Detaches monitored items from their current owners and recovers them on the visible node manager.
        /// </summary>
        /// <param name="nodeManager">The node manager associated with the operation.</param>
        /// <param name="monitoredItems">The monitored items included in the operation.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        internal async ValueTask RecoverMonitoredItemsAsync(
            IAsyncNodeManager nodeManager,
            IReadOnlyList<IMonitoredItem> monitoredItems,
            CancellationToken cancellationToken = default)
        {
            IAsyncNodeManager? visibleNodeManager = GetVisibleNodeManager(nodeManager);
            if (visibleNodeManager is not INodeManagerMonitoredItemLifecycle nodeManagerLifecycle)
            {
                return;
            }

            var failures = new List<Exception>();
            foreach (IMonitoredItem monitoredItem in monitoredItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var itemLifecycle = (IDetachableMonitoredItem)monitoredItem;
                if (!itemLifecycle.IsDetached)
                {
                    if (monitoredItem.NodeManager is not
                        INodeManagerMonitoredItemLifecycle currentLifecycle)
                    {
                        failures.Add(new InvalidOperationException(
                            "The current NodeManager cannot detach a deleted monitored item."));
                        continue;
                    }

                    ServiceResult detachResult = await currentLifecycle
                        .DetachMonitoredItemAsync(monitoredItem, cancellationToken)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(detachResult))
                    {
                        failures.Add(new ServiceResultException(detachResult));
                        continue;
                    }
                }

                ServiceResult attachResult = await nodeManagerLifecycle
                    .AttachMonitoredItemAsync(monitoredItem, cancellationToken)
                    .ConfigureAwait(false);
                if (ServiceResult.IsGood(attachResult))
                {
                    continue;
                }

                itemLifecycle.Detach(Server);
                itemLifecycle.MarkNodeDeleted();
                if (!IsExpectedRecoveryFailure(attachResult))
                {
                    failures.Add(new ServiceResultException(attachResult));
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "One or more monitored items could not be recovered.",
                    failures);
            }
        }

        private static bool IsExpectedRecoveryFailure(ServiceResult result)
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

        private IAsyncNodeManager? GetVisibleNodeManager(IAsyncNodeManager nodeManager)
        {
            return m_nodeManagers.Revision.TryGetGeneration(nodeManager, out NodeManagerGeneration? generation) &&
                generation!.Gate.State == NodeManagerGenerationState.Live
                    ? generation.NodeManager
                    : null;
        }

        /// <inheritdoc/>
        public void RegisterNamespaceManager(string namespaceUri, INodeManager nodeManager)
        {
            RegisterNamespaceManager(namespaceUri, nodeManager.ToAsyncNodeManager());
        }

        /// <inheritdoc/>
        public void RegisterNamespaceManager(string namespaceUri, IAsyncNodeManager nodeManager)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentNullException(nameof(namespaceUri));
            }

            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            IAsyncNodeManager? preparingNodeManager =
                GetPreparingNodeManager(nodeManager);
            ChangePreparer.StagingServer? staging = ChangePreparer.GetServer(preparingNodeManager ?? nodeManager);
            NamespaceTable namespaceTable = staging?.NamespaceUris ?? Server.NamespaceUris;
            int index = namespaceTable.GetIndex(namespaceUri);

            if (index < 0)
            {
                index = namespaceTable.Append(namespaceUri);
            }
            if (staging is { IsStaging: true })
            {
                staging.StageNamespaceRegistration(namespaceUri);
                return;
            }

            m_nodeManagers.RegisterNamespace(
                index,
                preparingNodeManager ?? nodeManager,
                visible: preparingNodeManager is null && staging?.IsStaging != true);
        }

        /// <inheritdoc/>
        public bool UnregisterNamespaceManager(string namespaceUri, INodeManager nodeManager)
        {
            return UnregisterNamespaceManager(namespaceUri, null, nodeManager);
        }

        /// <inheritdoc/>
        public bool UnregisterNamespaceManager(string namespaceUri, IAsyncNodeManager nodeManager)
        {
            return UnregisterNamespaceManager(namespaceUri, nodeManager, null);
        }

        private bool UnregisterNamespaceManager(string namespaceUri, IAsyncNodeManager? asyncNodeManager, INodeManager? nodeManager)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentNullException(nameof(namespaceUri));
            }

            if (nodeManager == null && asyncNodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            IAsyncNodeManager candidate = asyncNodeManager ?? nodeManager!.ToAsyncNodeManager();
            ChangePreparer.StagingServer? staging =
                ChangePreparer.GetServer(GetPreparingNodeManager(candidate) ?? candidate);
            if (staging is { IsStaging: true })
            {
                return staging.UnstageNamespaceRegistration(namespaceUri);
            }

            // look up the namespace uri.
            int namespaceIndex = Server.NamespaceUris.GetIndex(namespaceUri);
            if (namespaceIndex < 0)
            {
                return false;
            }

            return m_nodeManagers.UnregisterNamespace(
                namespaceIndex,
                asyncNodeManager,
                nodeManager);
        }

        private static void ValidatePreparedNodeManager(PreparedNodeManager prepared)
        {
            if (prepared is null)
            {
                throw new ArgumentNullException(nameof(prepared));
            }
            if (prepared.Published)
            {
                throw new InvalidOperationException(
                    "The prepared NodeManager has already been published.");
            }
        }

        private static void SetExistingEventSubscriptionSuppression(
            IAsyncNodeManager nodeManager,
            bool suppress)
        {
            if (nodeManager is AsyncCustomNodeManager asyncCustomNodeManager)
            {
                asyncCustomNodeManager.SuppressExistingEventSubscriptions = suppress;
            }
            else if (nodeManager.SyncNodeManager is CustomNodeManager2 customNodeManager)
            {
                customNodeManager.SuppressExistingEventSubscriptions = suppress;
            }
        }

        private IAsyncNodeManager? GetPreparingNodeManager(
            IAsyncNodeManager nodeManager)
        {
            lock (m_preparingNodeManagersLock)
            {
                return m_preparingNodeManagers.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate, nodeManager) ||
                    (candidate.SyncNodeManager is { } candidateSync &&
                        nodeManager.SyncNodeManager is { } nodeManagerSync &&
                        ReferenceEquals(
                            candidateSync,
                            nodeManagerSync)));
            }
        }

        private void SetPreparing(
            IAsyncNodeManager nodeManager,
            bool preparing)
        {
            lock (m_preparingNodeManagersLock)
            {
                if (preparing)
                {
                    m_preparingNodeManagers.Add(nodeManager);
                }
                else
                {
                    m_preparingNodeManagers.RemoveAll(candidate =>
                        ReferenceEquals(candidate, nodeManager));
                }
            }
        }

        private int[] ResolveNamespaceIndexes(IAsyncNodeManager nodeManager)
        {
            IEnumerable<string>? namespaceUris = nodeManager.NamespaceUris;
            if (namespaceUris is null)
            {
                return [];
            }
            return
            [
                .. namespaceUris
                    .Select(namespaceUri => (int)Server.NamespaceUris.GetIndexOrAppend(namespaceUri))
            ];
        }

        private async ValueTask AddExternalReferencesAsync(
            Dictionary<NodeId, IList<IReference>> externalReferences,
            IAsyncNodeManager additionalNodeManager,
            CancellationToken ct)
        {
            foreach (IAsyncNodeManager nodeManager in m_nodeManagers)
            {
                await nodeManager
                    .AddReferencesAsync(externalReferences, ct)
                    .ConfigureAwait(false);
            }

            if (!m_nodeManagers.Any(nodeManager =>
                ReferenceEquals(nodeManager, additionalNodeManager)))
            {
                await additionalNodeManager
                    .AddReferencesAsync(externalReferences, ct)
                    .ConfigureAwait(false);
            }
        }

        private StartupNodeManagerState? FindStartupApplicationNodeManager(
            IAsyncNodeManager nodeManager)
        {
            foreach (StartupNodeManagerState state in m_startupApplicationNodeManagers)
            {
                if (ReferenceEquals(state.NodeManager, nodeManager))
                {
                    return state;
                }
            }
            return null;
        }

        private static Dictionary<NodeId, Dictionary<IReference, int>>
            SnapshotExternalReferences(
                IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            var snapshot =
                new Dictionary<NodeId, Dictionary<IReference, int>>();
            foreach (KeyValuePair<NodeId, IList<IReference>> entry in externalReferences)
            {
                var references = new Dictionary<IReference, int>(
                    entry.Value.Count,
                    ReferenceEqualityComparer.Default);
                foreach (IReference reference in entry.Value)
                {
                    // Freeze values before the next manager can mutate a contributed reference.
                    var frozen = new NodeStateReference(
                        reference.ReferenceTypeId,
                        reference.IsInverse,
                        reference.TargetId);
                    references.TryGetValue(frozen, out int count);
                    references[frozen] = count + 1;
                }
                snapshot.Add(entry.Key, references);
            }
            return snapshot;
        }

        private static Dictionary<NodeId, IList<IReference>>
            CaptureAddedExternalReferences(
                Dictionary<NodeId, Dictionary<IReference, int>> before,
                IDictionary<NodeId, IList<IReference>> after)
        {
            var additions = new Dictionary<NodeId, IList<IReference>>();
            foreach (KeyValuePair<NodeId, IList<IReference>> entry in after)
            {
                before.TryGetValue(
                    entry.Key,
                    out Dictionary<IReference, int>? previous);

                foreach (IReference reference in entry.Value)
                {
                    if (previous is not null &&
                        previous.TryGetValue(reference, out int count) &&
                        count > 0)
                    {
                        previous[reference] = count - 1;
                        continue;
                    }

                    if (!additions.TryGetValue(
                        entry.Key,
                        out IList<IReference>? added))
                    {
                        additions.Add(entry.Key, added = []);
                    }
                    added.Add(reference);
                }
            }
            return additions;
        }

        private static Dictionary<NodeId, IList<IReference>> CloneExternalReferences(
            IDictionary<NodeId, IList<IReference>> source)
        {
            var clone = new Dictionary<NodeId, IList<IReference>>();
            foreach (KeyValuePair<NodeId, IList<IReference>> entry in source)
            {
                clone.Add(entry.Key, new List<IReference>(entry.Value));
            }
            return clone;
        }

        private static void RemoveExternalReferences(
            Dictionary<NodeId, IList<IReference>> retained,
            Dictionary<NodeId, IList<IReference>> owned)
        {
            foreach (KeyValuePair<NodeId, IList<IReference>> entry in owned)
            {
                if (!retained.TryGetValue(
                    entry.Key,
                    out IList<IReference>? retainedReferences))
                {
                    continue;
                }

                foreach (IReference reference in entry.Value)
                {
                    for (int ii = 0; ii < retainedReferences.Count; ii++)
                    {
                        if (ReferenceEqualityComparer.Default.Equals(
                            retainedReferences[ii],
                            reference))
                        {
                            retainedReferences.RemoveAt(ii);
                            break;
                        }
                    }
                }

                if (retainedReferences.Count == 0)
                {
                    retained.Remove(entry.Key);
                }
            }
        }

        private async ValueTask RemoveExternalReferencesAsync(
            Dictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken ct)
        {
            var referencesToRemove = new List<LocalReference>();
            foreach (KeyValuePair<NodeId, IList<IReference>> entry in externalReferences)
            {
                foreach (IReference reference in entry.Value)
                {
                    var targetId = ExpandedNodeId.ToNodeId(
                        reference.TargetId,
                        Server.NamespaceUris);
                    if (targetId.IsNull)
                    {
                        continue;
                    }

                    referencesToRemove.Add(new LocalReference(
                        entry.Key,
                        reference.ReferenceTypeId,
                        reference.IsInverse,
                        targetId));
                }
            }

            if (referencesToRemove.Count > 0)
            {
                await RemoveReferencesAsync(referencesToRemove, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The server that the node manager belongs to.
        /// </summary>
        protected IServerInternal Server { get; }

        /// <summary>
        /// Gets the server-owned reconciler shared by lifecycle and request binding dispatch.
        /// </summary>
        internal GenerationReconciler GenerationReconciler { get; }

        /// <inheritdoc/>
        GenerationReconciler IDynamicNodeManagerHost.GenerationReconciler => GenerationReconciler;

        /// <inheritdoc/>
        public IReadOnlyList<IAsyncNodeManager> AsyncNodeManagers => [.. m_nodeManagers];

        /// <summary>
        /// Gets the number of node managers whose shutdown has completed.
        /// </summary>
        internal int ShutdownCompletedNodeManagerCount =>
            Volatile.Read(ref m_shutdownCompletedNodeManagerCount);

        /// <inheritdoc/>
        public IReadOnlyList<INodeManager> NodeManagers
            => [.. m_nodeManagers.Select(manager => manager.SyncNodeManager)];

        /// <summary>
        /// The namespace managers being managed
        /// </summary>
        internal IReadOnlyDictionary<int, IReadOnlyList<IAsyncNodeManager>> NamespaceManagers
            => m_nodeManagers.NamespaceManagers;

        /// <summary>
        /// Attempts to resolve the exact generation in the current address-space view.
        /// </summary>
        /// <param name="nodeManager">The manager expected in the current view.</param>
        /// <param name="generation">The exact current generation, when present.</param>
        /// <returns><c>true</c> when the current view contains the manager.</returns>
        internal bool TryGetCurrentGeneration(
            IAsyncNodeManager nodeManager,
            out NodeManagerGeneration? generation)
        {
            return m_nodeManagers.Revision.TryGetGeneration(nodeManager, out generation);
        }

        /// <summary>
        /// Retains an admitted node-manager generation while a control-plane callback is dispatched.
        /// </summary>
        internal sealed class NotificationDispatchLease : IDisposable
        {
            /// <summary>
            /// Captures one generation admission and the monitored items covered by the dispatch.
            /// </summary>
            /// <param name="owner">The master node manager responsible for releasing this dispatch lease.</param>
            /// <param name="generation">The generation admitted by the lease.</param>
            /// <param name="monitoredItems">The monitored items included in the operation.</param>
            /// <param name="slot">The striped generation gate slot released by this lease.</param>
            public NotificationDispatchLease(
                MasterNodeManager owner,
                NodeManagerGeneration generation,
                IEventMonitoredItem[] monitoredItems,
                int slot)
            {
                Generation = generation;
                MonitoredItems = monitoredItems;
                m_origin = new SourceEmissionOrigin(owner, generation, slot);
            }

            /// <summary>
            /// Gets the node manager receiving the notification dispatch.
            /// </summary>
            public IAsyncNodeManager NodeManager => Generation.NodeManager;

            /// <summary>
            /// Gets the exact generation protected by this lease.
            /// </summary>
            public NodeManagerGeneration Generation { get; }

            /// <summary>
            /// Gets the monitored items included in this dispatch.
            /// </summary>
            public IEventMonitoredItem[] MonitoredItems { get; }

            /// <summary>
            /// Gets whether the lease still retains its dispatch owner.
            /// </summary>
            public bool IsActive => m_origin.IsActive;

            /// <summary>
            /// Creates invocation-scoped targets that carry this dispatch's exact source admission.
            /// </summary>
            /// <param name="context">The request identity that authorized the refresh.</param>
            /// <returns>The event targets for this callback.</returns>
            internal IEventMonitoredItem[] CreateRefreshTargets(OperationContext context)
            {
                var targets = new IEventMonitoredItem[MonitoredItems.Length];
                for (int ii = 0; ii < targets.Length; ii++)
                {
                    targets[ii] = new ConditionRefreshTarget(
                        MonitoredItems[ii], m_origin, context.Session, context.NotificationAuthorization);
                }
                return targets;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                m_origin.Dispose();
            }

            /// <summary>
            /// Owns the dispatch admission and revokes any escaped refresh targets.
            /// </summary>
            private readonly SourceEmissionOrigin m_origin;
        }

        private sealed class StartupNodeManagerState
        {
            public StartupNodeManagerState(IAsyncNodeManager nodeManager)
            {
                NodeManager = nodeManager;
            }

            public IAsyncNodeManager NodeManager { get; }

            public Dictionary<NodeId, IList<IReference>>? ExternalReferences { get; set; }
        }

        private readonly ILogger m_logger;
        private readonly SemaphoreSlim m_dynamicMutationSemaphore = new(1, 1);
        private readonly SemaphoreSlim m_startupShutdownSemaphoreSlim = new(1, 1);
        private readonly NodeManagerRoutingTable m_nodeManagers;
        private readonly HashSet<object> m_shutdownCompletedNodeManagers =
            new(RefEqualityComparer.Default);
        private int m_shutdownCompletedNodeManagerCount;
        private readonly List<IAsyncNodeManager> m_preparingNodeManagers = [];
        private readonly Lock m_preparingNodeManagersLock = new();
        private readonly List<StartupNodeManagerState>
            m_startupApplicationNodeManagers = [];
        private volatile Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>>
            m_dynamicExternalReferences = [];
        private Dictionary<NodeId, IList<IReference>>? m_startupExternalReferences;

        private readonly Lock m_retiringEventGenerationsLock = new();
        private NodeManagerGeneration[] m_retiringEventGenerations = [];
        private volatile Action? m_retiredGenerationDrainObserver;

        private bool m_startupApplicationNodeManagersTransferred;
        private bool m_disposed;

        /// <summary>
        /// Protects publication of the single node-manager disposal task.
        /// </summary>
        private readonly Lock m_disposalLock = new();

        /// <summary>
        /// Allows repeated asynchronous disposal calls to await the same cleanup work.
        /// </summary>
        private Task m_disposalTask = Task.CompletedTask;
    }

    /// <summary>
    /// Stores a reference between NodeManagers that is needs to be created or deleted.
    /// </summary>
    public class LocalReference
    {
        /// <summary>
        /// Initializes the reference.
        /// </summary>
        public LocalReference(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            NodeId targetId)
        {
            SourceId = sourceId;
            ReferenceTypeId = referenceTypeId;
            IsInverse = isInverse;
            TargetId = targetId;
        }

        /// <summary>
        /// The source of the reference.
        /// </summary>
        public NodeId SourceId { get; }

        /// <summary>
        /// The type of reference.
        /// </summary>
        public NodeId ReferenceTypeId { get; }

        /// <summary>
        /// True if the reference is an inverse reference.
        /// </summary>
        public bool IsInverse { get; }

        /// <summary>
        /// The target of the reference.
        /// </summary>
        public NodeId TargetId { get; }
    }

    /// <summary>
    /// Represents a generator for unique monitored item ids.
    /// Call next() to retrieve the next valid monitoredItemId.
    /// </summary>
    /// <remarks>This class provides a mechanism to generate sequential ids for monitored
    /// items. It is designed to ensure thread-safe incrementation of the identifier.</remarks>
    public class MonitoredItemIdFactory
    {
        /// <summary>
        /// Advances the identifier floor without moving the current identifier backwards during restoration.
        /// </summary>
        /// <param name="firstId"></param>
        public void SetStartValue(uint firstId)
        {
            Utils.SetIdentifierToAtLeast(ref m_lastMonitoredItemId, firstId);
        }

        /// <summary>
        /// Get the next unique monitored item id.
        /// </summary>
        /// <returns>an uint that can be used as an id for a monitored item</returns>
        public uint GetNextId()
        {
            return Utils.IncrementIdentifier(ref m_lastMonitoredItemId);
        }

        private uint m_lastMonitoredItemId;
    }

    /// <summary>
    /// Source-generated log messages for MasterNodeManager.
    /// </summary>
    internal static partial class MasterNodeManagerLog
    {
        /// <summary>
        /// Logs the number of node managers participating in startup.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="count">The number of affected items.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 0, Level = LogLevel.Information,
            Message = "MasterNodeManager.Startup - NodeManagers={Count}")]
        public static partial void MasterNodeManagerStartupNodeManagersCount(this ILogger logger, int count);

        /// <summary>
        /// Logs an unexpected node-manager address-space creation failure.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="nodeManager">The node manager associated with the operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 1, Level = LogLevel.Error,
            Message = "Unexpected error creating address space for NodeManager ={NodeManager}.")]
        public static partial void UnexpectedErrorCreatingAddressSpaceForNodeManager(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        /// <summary>
        /// Logs an unexpected failure while a node manager adds references.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="nodeManager">The node manager associated with the operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 2, Level = LogLevel.Error,
            Message = "Unexpected error adding references for NodeManager ={NodeManager}.")]
        public static partial void UnexpectedErrorAddingReferencesForNodeManagerNodeManager(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        /// <summary>
        /// Logs an unexpected failure while notifying a node manager of session closure.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="nodeManager">The node manager associated with the operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 3, Level = LogLevel.Error,
            Message = "Unexpected error closing session for NodeManager ={NodeManager}.")]
        public static partial void UnexpectedErrorClosingSessionForNodeManagerNodeManager(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        /// <summary>
        /// Logs an unexpected failure while notifying a node manager of session activation.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="nodeManager">The node manager associated with the operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 4, Level = LogLevel.Error,
            Message = "Unexpected error notifying node manager of session activation for NodeManager={NodeManager}.")]
        public static partial void UnexpectedErrorNotifyingNodeManagerOfSession(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        /// <summary>
        /// Logs the number of node managers participating in shutdown.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="count">The number of affected items.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 5, Level = LogLevel.Information,
            Message = "MasterNodeManager.Shutdown - NodeManagers={Count}")]
        public static partial void MasterNodeManagerShutdownNodeManagersCount(this ILogger logger, int count);

        /// <summary>
        /// Logs a bad status returned when mirroring an inverse reference to its owning node manager.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="refType">The reference type involved in the failed operation.</param>
        /// <param name="source">The source node or reference source involved in the operation.</param>
        /// <param name="target">The reference target involved in the failed operation.</param>
        /// <param name="status">The status returned by the failed operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 6, Level = LogLevel.Warning,
            Message = "AddReferences: failed to mirror inverse edge {RefType} from {Source} to {Target} on " +
                "owning NodeManager: {Status}")]
        public static partial void AddReferencesFailedToMirrorInverseEdgeRefType(
            this ILogger logger,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target,
            StatusCode status);

        /// <summary>
        /// Logs an exception thrown when mirroring an inverse reference to its owning node manager.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="refType">The reference type involved in the failed operation.</param>
        /// <param name="source">The source node or reference source involved in the operation.</param>
        /// <param name="target">The reference target involved in the failed operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 7, Level = LogLevel.Warning,
            Message = "AddReferences: failed to mirror inverse edge {RefType} from {Source} to {Target} on " +
                "owning NodeManager.")]
        public static partial void AddReferencesFailedToMirrorInverseEdgeRefType2(
            this ILogger logger,
            Exception ex,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target);

        /// <summary>
        /// Logs a bad status returned when mirroring an inverse reference deletion.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="refType">The reference type involved in the failed operation.</param>
        /// <param name="source">The source node or reference source involved in the operation.</param>
        /// <param name="target">The reference target involved in the failed operation.</param>
        /// <param name="status">The status returned by the failed operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 8, Level = LogLevel.Warning,
            Message = "DeleteReferences: failed to mirror inverse delete {RefType} from {Source} to {Target} " +
                "on owning NodeManager: {Status}")]
        public static partial void DeleteReferencesFailedToMirrorInverseDeleteRefType(
            this ILogger logger,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target,
            StatusCode status);

        /// <summary>
        /// Logs an exception thrown when mirroring an inverse reference deletion.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="refType">The reference type involved in the failed operation.</param>
        /// <param name="source">The source node or reference source involved in the operation.</param>
        /// <param name="target">The reference target involved in the failed operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 9, Level = LogLevel.Warning,
            Message = "DeleteReferences: failed to mirror inverse delete {RefType} from {Source} to {Target} " +
                "on owning NodeManager.")]
        public static partial void DeleteReferencesFailedToMirrorInverseDeleteRefType2(
            this ILogger logger,
            Exception ex,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target);

        /// <summary>
        /// Logs the number of nodes submitted for registration.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="count">The number of affected items.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 10, Level = LogLevel.Trace,
            Message = "MasterNodeManager.RegisterNodes - Count={Count}")]
        public static partial void MasterNodeManagerRegisterNodesCountCount(this ILogger logger, int count);

        /// <summary>
        /// Logs the number of nodes submitted for unregistration.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="count">The number of affected items.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 11, Level = LogLevel.Trace,
            Message = "MasterNodeManager.UnregisterNodes - Count={Count}")]
        public static partial void MasterNodeManagerUnregisterNodesCountCount(this ILogger logger, int count);

        /// <summary>
        /// Logs an unexpected browse-path translation failure.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 12, Level = LogLevel.Error,
            Message = "Unexpected error translating browse path.")]
        public static partial void UnexpectedErrorTranslatingBrowsePath(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs the number of node values submitted for reading.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="count">The number of affected items.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 13, Level = LogLevel.Trace,
            Message = "MasterNodeManager.Read - Count={Count}")]
        public static partial void MasterNodeManagerReadCountCount(this ILogger logger, int count);

        /// <summary>
        /// Logs failure of a node manager's asynchronous condition refresh.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 14, Level = LogLevel.Error,
            Message = "Error calling ConditionRefreshAsync on AsyncNodeManager.")]
        public static partial void ErrorCallingConditionRefreshAsyncOnAsyncNodeManager(
            this ILogger logger,
            Exception ex);

        /// <summary>
        /// Logs an exception thrown when a node manager subscribes to all events.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="nodeManager">The node manager associated with the operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 15, Level = LogLevel.Error,
            Message = "NodeManager threw an exception subscribing to all events. NodeManager={NodeManager}")]
        public static partial void NodeManagerThrewAnExceptionSubscribingToAll(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        /// <summary>
        /// Logs failure to populate a monitored item's initial queue.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="monitoredItemId">The identifier of the affected monitored item.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 16, Level = LogLevel.Error,
            Message = "Failed to pre-hydrate queue for monitored item with id {MonitoredItemId}")]
        public static partial void FailedToPreHydrateQueueForMonitored(
            this ILogger logger,
            Exception ex,
            uint monitoredItemId);

        /// <summary>
        /// Logs a node-manager exception during monitored-item transfer.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="nodeManager">The node manager associated with the operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 23, Level = LogLevel.Error,
            Message = "NodeManager threw an exception transferring monitored items. NodeManager={NodeManager}")]
        public static partial void MonitoredItemTransferFailedForNodeManager(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        /// <summary>
        /// Reports a monitored-item operation failure in its owning node manager.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 24, Level = LogLevel.Error,
            Message = "NodeManager failed a monitored-item operation. NodeManager={NodeManager}")]
        public static partial void MonitoredItemOwnerDispatchFailed(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        /// <summary>
        /// Logs a failed node-management operation.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 25, Level = LogLevel.Error,
            Message = "NodeManager failed a node-management operation.")]
        public static partial void NodeManagementOperationFailed(this ILogger logger, Exception ex);

        /// <summary>
        /// Logs an authorization check in which the current user has no granted role.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 17, Level = LogLevel.Debug,
            Message = "Current user has no granted role.")]
        public static partial void CurrentUserHasNoGrantedRole(this ILogger logger);

        /// <summary>
        /// Logs a node permission check that the user's granted permissions do not satisfy.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="nodeId">The identifier of the affected source node.</param>
        /// <param name="requestedPermission">The permission required by the operation.</param>
        /// <param name="userPermissions">The permissions granted to the current user.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 18, Level = LogLevel.Debug,
            Message = "Role permissions validation failed for node {NodeId}. Requested: {RequestedPermission}, " +
                "User has: {UserPermissions}")]
        public static partial void RolePermissionsValidationFailedForNodeNodeId(
            this ILogger logger,
            NodeId nodeId,
            PermissionType requestedPermission,
            PermissionType userPermissions);

        /// <summary>
        /// Logs a bad status returned when rolling back an added reference.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="refType">The reference type involved in the failed operation.</param>
        /// <param name="source">The source node or reference source involved in the operation.</param>
        /// <param name="target">The reference target involved in the failed operation.</param>
        /// <param name="status">The status returned by the failed operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 19, Level = LogLevel.Error,
            Message = "AddReferences: failed to roll back {RefType} from {Source} to {Target}: {Status}")]
        public static partial void AddReferencesRollbackFailed(
            this ILogger logger,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target,
            StatusCode status);

        /// <summary>
        /// Logs an exception thrown when rolling back an added reference.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="refType">The reference type involved in the failed operation.</param>
        /// <param name="source">The source node or reference source involved in the operation.</param>
        /// <param name="target">The reference target involved in the failed operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 20, Level = LogLevel.Error,
            Message = "AddReferences: failed to roll back {RefType} from {Source} to {Target}.")]
        public static partial void AddReferencesRollbackFailed2(
            this ILogger logger,
            Exception ex,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target);

        /// <summary>
        /// Logs a bad status returned when restoring a deleted reference.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="refType">The reference type involved in the failed operation.</param>
        /// <param name="source">The source node or reference source involved in the operation.</param>
        /// <param name="target">The reference target involved in the failed operation.</param>
        /// <param name="status">The status returned by the failed operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 21, Level = LogLevel.Error,
            Message = "DeleteReferences: failed to restore {RefType} from {Source} to {Target}: {Status}")]
        public static partial void DeleteReferencesRollbackFailed(
            this ILogger logger,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target,
            StatusCode status);

        /// <summary>
        /// Logs an exception thrown when restoring a deleted reference.
        /// </summary>
        /// <param name="logger">The logger receiving the diagnostic messages.</param>
        /// <param name="ex">The exception that caused the failure.</param>
        /// <param name="refType">The reference type involved in the failed operation.</param>
        /// <param name="source">The source node or reference source involved in the operation.</param>
        /// <param name="target">The reference target involved in the failed operation.</param>
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 22, Level = LogLevel.Error,
            Message = "DeleteReferences: failed to restore {RefType} from {Source} to {Target}.")]
        public static partial void DeleteReferencesRollbackFailed2(
            this ILogger logger,
            Exception ex,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target);
    }
}
