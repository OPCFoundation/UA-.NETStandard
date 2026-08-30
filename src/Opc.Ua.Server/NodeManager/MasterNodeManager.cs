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
        IMasterNodeManager,
        IMonitoredItemTransferCoordinator,
        IDynamicNodeManagerHost,
        ISyncNodeManagerMonitoredItemRecovery
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
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
                }
            }
            if (additionalSyncManagers != null)
            {
                foreach (INodeManager nodeManager in additionalSyncManagers)
                {
                    RegisterNodeManager(nodeManager.ToAsyncNodeManager(), registeredManagers, namespaceManagers);
                }
            }

            // Publish the initial manager and namespace routing snapshot.
            m_nodeManagers.Initialize(namespaceManagers);

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
            if (disposing && !m_disposed)
            {
                m_disposed = true;

                m_startupShutdownSemaphoreSlim.Wait();

                List<IAsyncNodeManager> nodeManagers = [.. m_nodeManagers];
                m_nodeManagers.Clear();
                m_dynamicExternalReferences.Clear();

                foreach (IAsyncNodeManager nodeManager in nodeManagers)
                {
                    (nodeManager as IDisposable)?.Dispose();
                }

                m_startupShutdownSemaphoreSlim.Dispose();
                m_dynamicMutationSemaphore.Dispose();
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
                    try
                    {
                        await nodeManager.CreateAddressSpaceAsync(externalReferences, cancellationToken)
                            .ConfigureAwait(false);
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
                            await dispatch.NodeManager.SessionClosingAsync(
                                context,
                                sessionId,
                                deleteSubscriptions,
                                cancellationToken).ConfigureAwait(false);
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
            try
            {
                foreach (NotificationDispatchLease dispatch in dispatches)
                {
                    try
                    {
                        await dispatch.NodeManager.SessionActivatedAsync(
                            context,
                            sessionId,
                            cancellationToken).ConfigureAwait(false);
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

        async ValueTask<PreparedNodeManager> IDynamicNodeManagerHost.PrepareAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            await m_startupShutdownSemaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
            bool prepared = false;
            try
            {
                SetPreparing(nodeManager, preparing: true);
                SetExistingEventSubscriptionSuppression(nodeManager, suppress: true);
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await nodeManager
                    .CreateAddressSpaceAsync(externalReferences, ct)
                    .ConfigureAwait(false);
                prepared = true;
                return new PreparedNodeManager(nodeManager, externalReferences);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                m_nodeManagers.RemoveNamespaceManager(nodeManager);
                try
                {
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
                if (!prepared)
                {
                    SetPreparing(nodeManager, preparing: false);
                }
                SetExistingEventSubscriptionSuppression(nodeManager, suppress: false);
                m_startupShutdownSemaphoreSlim.Release();
            }
        }

        async ValueTask IDynamicNodeManagerHost.PublishAsync(
            PreparedNodeManager prepared,
            CancellationToken ct)
        {
            ValidatePreparedNodeManager(prepared);

            await m_startupShutdownSemaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (prepared.Staged)
                {
                    throw new InvalidOperationException(
                        "The prepared NodeManager has already been staged.");
                }
                if (m_dynamicExternalReferences.ContainsKey(
                    prepared.NodeManager))
                {
                    throw new InvalidOperationException(
                        "The NodeManager is already registered.");
                }
                prepared.Staged = true;
            }
            finally
            {
                m_startupShutdownSemaphoreSlim.Release();
            }
        }

        async ValueTask IDynamicNodeManagerHost.ReplaceAsync(
            IAsyncNodeManager current,
            PreparedNodeManager replacement,
            bool allowActiveMonitoredItems,
            bool retainReplacedNotifications,
            CancellationToken ct)
        {
            if (current is null)
            {
                throw new ArgumentNullException(nameof(current));
            }
            ValidatePreparedNodeManager(replacement);

            await m_startupShutdownSemaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!m_dynamicExternalReferences.TryGetValue(
                    current,
                    out Dictionary<NodeId, IList<IReference>>? currentExternalReferences))
                {
                    throw new InvalidOperationException(
                        "The NodeManager is not owned by the live lifecycle provider.");
                }
                if (replacement.Staged)
                {
                    throw new InvalidOperationException(
                        "The replacement NodeManager has already been staged.");
                }
                replacement.ReplacedNodeManager = current;
                replacement.ReplacedExternalReferences = currentExternalReferences;
                replacement.AllowActiveMonitoredItems = allowActiveMonitoredItems;
                replacement.RetainReplacedNotifications =
                    retainReplacedNotifications;
                replacement.Staged = true;
            }
            finally
            {
                m_startupShutdownSemaphoreSlim.Release();
            }
        }

        async ValueTask IDynamicNodeManagerHost.CommitAsync(
            PreparedNodeManager prepared,
            Func<ValueTask>? beforeCommit,
            Func<ValueTask>? afterCommit,
            Func<ValueTask>? rollbackCommit,
            CancellationToken ct)
        {
            ValidatePreparedNodeManager(prepared);
            if (!prepared.Staged)
            {
                throw new InvalidOperationException(
                    "The prepared NodeManager has not been staged.");
            }

            await m_dynamicMutationSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                bool committed = false;
                bool transitionStarted = false;
                try
                {
                    if (beforeCommit is not null)
                    {
                        transitionStarted = true;
                        await beforeCommit().ConfigureAwait(false);
                    }
                    await CommitPreparedNodeManagerAsync(prepared, ct)
                        .ConfigureAwait(false);
                    committed = true;
                    if (afterCommit is not null)
                    {
                        await afterCommit().ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    if (!committed && prepared.Published)
                    {
                        if (afterCommit is not null)
                        {
                            try
                            {
                                await afterCommit().ConfigureAwait(false);
                            }
                            catch (Exception repairException) when (
                                repairException is not OutOfMemoryException)
                            {
                                throw new AggregateException(
                                    "The retained NodeManager replacement and post-commit " +
                                    "monitored-item repair both failed.",
                                    ex,
                                    repairException);
                            }
                        }
                        throw;
                    }

                    if (!committed &&
                        transitionStarted &&
                        rollbackCommit is not null)
                    {
                        try
                        {
                            await rollbackCommit().ConfigureAwait(false);
                        }
                        catch (Exception rollbackException) when (
                            rollbackException is not OutOfMemoryException)
                        {
                            throw new AggregateException(
                                "NodeManager commit and monitored-item rollback both failed.",
                                ex,
                                rollbackException);
                        }
                    }
                    throw;
                }
            }
            finally
            {
                m_dynamicMutationSemaphore.Release();
            }
        }

        private async ValueTask CommitPreparedNodeManagerAsync(
            PreparedNodeManager prepared,
            CancellationToken ct)
        {
            await m_startupShutdownSemaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (prepared.ReplacedNodeManager is null)
                {
                    await CommitAddAsync(prepared).ConfigureAwait(false);
                }
                else
                {
                    if (!prepared.AllowActiveMonitoredItems)
                    {
                        EnsureNoActiveMonitoredItems(
                            prepared.ReplacedNodeManager);
                    }
                    await CommitReplacementAsync(prepared).ConfigureAwait(false);
                }
                prepared.Staged = false;
                prepared.Published = true;
                prepared.ReplacedNodeManager = null;
                prepared.ReplacedExternalReferences = null;
                SetPreparing(prepared.NodeManager, preparing: false);
            }
            finally
            {
                m_startupShutdownSemaphoreSlim.Release();
            }
        }

        async ValueTask IDynamicNodeManagerHost.UnpublishAsync(
            IAsyncNodeManager nodeManager,
            Func<ValueTask>? beforeUnpublish,
            Func<ValueTask>? rollbackUnpublish,
            CancellationToken ct)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            await m_dynamicMutationSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                bool transitionStarted = false;
                try
                {
                    if (beforeUnpublish is not null)
                    {
                        transitionStarted = true;
                        await beforeUnpublish().ConfigureAwait(false);
                    }

                    await m_startupShutdownSemaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
                    bool routeRemoved = false;
                    bool referenceMutationStarted = false;
                    bool wasVisible = m_nodeManagers.IsVisible(nodeManager);
                    try
                    {
                        if (!m_dynamicExternalReferences.TryGetValue(
                            nodeManager,
                            out Dictionary<NodeId, IList<IReference>>? externalReferences))
                        {
                            throw new InvalidOperationException(
                                "The NodeManager is not owned by the live lifecycle provider.");
                        }

                        EnsureNoActiveMonitoredItems(nodeManager);
                        referenceMutationStarted = true;
                        await RemoveExternalReferencesAsync(
                            externalReferences,
                            CancellationToken.None).ConfigureAwait(false);
                        m_nodeManagers.Remove(nodeManager);
                        routeRemoved = true;
                        m_dynamicExternalReferences.Remove(nodeManager);
                    }
                    catch
                    {
                        if (routeRemoved)
                        {
                            m_nodeManagers.Add(
                                nodeManager,
                                ResolveNamespaceIndexes(nodeManager),
                                visible: false);
                        }
                        if (referenceMutationStarted &&
                            m_dynamicExternalReferences.TryGetValue(
                                nodeManager,
                                out Dictionary<NodeId, IList<IReference>>? externalReferences))
                        {
                            await AddExternalReferencesAsync(
                                externalReferences,
                                nodeManager,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        if (routeRemoved)
                        {
                            m_nodeManagers.SetVisible(
                                nodeManager,
                                wasVisible);
                        }
                        throw;
                    }
                    finally
                    {
                        m_startupShutdownSemaphoreSlim.Release();
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    if (transitionStarted && rollbackUnpublish is not null)
                    {
                        try
                        {
                            await rollbackUnpublish().ConfigureAwait(false);
                        }
                        catch (Exception rollbackException) when (
                            rollbackException is not OutOfMemoryException)
                        {
                            throw new AggregateException(
                                "NodeManager unpublish and monitored-item rollback both failed.",
                                ex,
                                rollbackException);
                        }
                    }
                    throw;
                }
            }
            finally
            {
                m_dynamicMutationSemaphore.Release();
            }
        }

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
            await m_startupShutdownSemaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await nodeManager.DeleteAddressSpaceAsync(ct).ConfigureAwait(false);
                RemoveRetiredGenerationNotifications(nodeManager);
            }
            finally
            {
                m_startupShutdownSemaphoreSlim.Release();
            }
        }

        async ValueTask IDynamicNodeManagerHost.RemoveDestroyedExternalReferencesAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            await m_startupShutdownSemaphoreSlim.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                List<LocalReference> referencesToRemove = nodeManager switch
                {
                    AsyncCustomNodeManager asyncCustomNodeManager =>
                        asyncCustomNodeManager.GetRemovedExternalReferences(),
                    _ when nodeManager.SyncNodeManager is
                        CustomNodeManager2 customNodeManager =>
                        customNodeManager.GetRemovedExternalReferences(),
                    _ => []
                };
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
            finally
            {
                m_startupShutdownSemaphoreSlim.Release();
            }
        }

        async ValueTask IDynamicNodeManagerHost.RollbackAsync(
            PreparedNodeManager prepared,
            CancellationToken ct)
        {
            ValidatePreparedNodeManager(prepared, allowPublished: true);

            if (prepared.Published)
            {
                await ((IDynamicNodeManagerHost)this)
                    .UnpublishAsync(prepared.NodeManager, ct: ct)
                    .ConfigureAwait(false);
                prepared.Published = false;
            }
            prepared.Staged = false;
            m_nodeManagers.RemoveNamespaceManager(prepared.NodeManager);
            SetPreparing(prepared.NodeManager, preparing: false);

            await ((IDynamicNodeManagerHost)this)
                .DestroyAddressSpaceAsync(
                    prepared.NodeManager,
                    ct: ct)
                .ConfigureAwait(false);
        }

        void IDynamicNodeManagerHost.Release(IAsyncNodeManager nodeManager)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            RemoveRetiredGenerationNotifications(nodeManager);
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

        void IDynamicNodeManagerHost.SetRetiredGenerationNotifications(
            IAsyncNodeManager nodeManager,
            bool enabled)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            lock (m_retiredGenerationNotificationsLock)
            {
                RetiredGenerationNotifications? notifications =
                    m_retiredGenerationNotifications.FirstOrDefault(candidate =>
                        ReferenceEquals(candidate.NodeManager, nodeManager));
                NotificationDispatchState? dispatchState =
                    notifications?.DispatchState ??
                    m_notificationDispatchStates.FirstOrDefault(candidate =>
                        candidate.References(nodeManager));
                if (dispatchState is not null)
                {
                    dispatchState.Enabled = enabled;
                }
                if (notifications is not null)
                {
                    notifications.Enabled = enabled;
                    notifications.AcceptEventDeletes = enabled;
                }
            }
        }

        async ValueTask IDynamicNodeManagerHost.WaitForNotificationDispatchesAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            await WaitForNotificationDispatchesAsync(nodeManager, ct)
                .ConfigureAwait(false);
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

            await WaitForNotificationDispatchesAsync(nodeManager, ct)
                .ConfigureAwait(false);

            RetiredGenerationNotifications? notifications;
            lock (m_retiredGenerationNotificationsLock)
            {
                notifications = m_retiredGenerationNotifications.FirstOrDefault(
                    candidate => ReferenceEquals(candidate.NodeManager, nodeManager));
            }
            if (notifications is null)
            {
                return;
            }

            IEventMonitoredItem[] monitoredItems;
            lock (m_retiredGenerationNotificationsLock)
            {
                if (!m_retiredGenerationNotifications.Contains(notifications))
                {
                    return;
                }
                monitoredItems = [.. notifications.SubscribedEventMonitoredItems];
            }

            foreach (IEventMonitoredItem monitoredItem in monitoredItems)
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
                lock (m_retiredGenerationNotificationsLock)
                {
                    if (m_retiredGenerationNotifications.Contains(notifications))
                    {
                        notifications.SubscribedEventMonitoredItems.RemoveAll(
                            candidate => ReferenceEquals(candidate, monitoredItem));
                    }
                }
            }
        }

        private async ValueTask WaitForNotificationDispatchesAsync(
            IAsyncNodeManager nodeManager,
            CancellationToken ct)
        {
            if (nodeManager is null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            Task dispatchesDrained;
            lock (m_retiredGenerationNotificationsLock)
            {
                RetiredGenerationNotifications? notifications =
                    m_retiredGenerationNotifications.FirstOrDefault(
                        candidate => ReferenceEquals(candidate.NodeManager, nodeManager));
                NotificationDispatchState dispatchState =
                    notifications?.DispatchState ??
                    GetOrCreateNotificationDispatchState(nodeManager);
                dispatchState.Enabled = false;
                if (notifications is not null)
                {
                    notifications.Enabled = false;
                    notifications.AcceptEventDeletes = false;
                }

                dispatchesDrained = dispatchState.ActiveDispatches == 0
                    ? Task.CompletedTask
                    : (dispatchState.DispatchesDrained ??=
                        new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            await dispatchesDrained.WaitAsync(ct).ConfigureAwait(false);
        }

        private void RetainRetiredGenerationNotifications(
            IAsyncNodeManager nodeManager)
        {
            List<IEventMonitoredItem> monitoredItems =
            [
                .. Server.EventManager.GetMonitoredItems().Where(
                    monitoredItem => monitoredItem.MonitoringAllEvents)
            ];
            lock (m_retiredGenerationNotificationsLock)
            {
                if (m_retiredGenerationNotifications.Any(candidate =>
                    ReferenceEquals(candidate.NodeManager, nodeManager)))
                {
                    throw new InvalidOperationException(
                        "The NodeManager is already retained for lifecycle notifications.");
                }
                NotificationDispatchState dispatchState =
                    GetOrCreateNotificationDispatchState(nodeManager);
                var notifications = new RetiredGenerationNotifications(
                    nodeManager,
                    dispatchState,
                    monitoredItems);
                dispatchState.Notifications = notifications;
                m_retiredGenerationNotifications.Add(notifications);
            }
        }

        private void RemoveRetiredGenerationNotifications(
            IAsyncNodeManager nodeManager)
        {
            lock (m_retiredGenerationNotificationsLock)
            {
                for (int ii = m_retiredGenerationNotifications.Count - 1; ii >= 0; ii--)
                {
                    RetiredGenerationNotifications notifications =
                        m_retiredGenerationNotifications[ii];
                    if (ReferenceEquals(notifications.NodeManager, nodeManager))
                    {
                        m_retiredGenerationNotifications.RemoveAt(ii);
                        if (ReferenceEquals(
                            notifications.DispatchState.Notifications,
                            notifications))
                        {
                            notifications.DispatchState.Notifications = null;
                        }
                        if (notifications.DispatchState.Enabled &&
                            notifications.DispatchState.ActiveDispatches == 0)
                        {
                            m_notificationDispatchStates.Remove(notifications.DispatchState);
                        }
                    }
                }
                m_notificationDispatchStates.RemoveAll(dispatchState =>
                    dispatchState.Enabled &&
                    dispatchState.ActiveDispatches == 0 &&
                    dispatchState.References(nodeManager));
            }
        }

        private NotificationDispatchLease[] GetSessionNotificationDispatches(
            IReadOnlyList<IAsyncNodeManager> activeNodeManagers)
        {
            var dispatches = new List<NotificationDispatchLease>();
            lock (m_retiredGenerationNotificationsLock)
            {
                foreach (IAsyncNodeManager nodeManager in activeNodeManagers)
                {
                    AddActiveNotificationDispatch(dispatches, nodeManager);
                }
                foreach (RetiredGenerationNotifications notifications in
                    m_retiredGenerationNotifications)
                {
                    if (notifications.Enabled &&
                        !ContainsNodeManager(dispatches, notifications.NodeManager))
                    {
                        dispatches.Add(
                            CreateRetiredNotificationDispatch(notifications));
                    }
                }
            }
            return [.. dispatches];
        }

        internal NotificationDispatchLease[] GetAllEventNotificationDispatches(
            IReadOnlyList<IAsyncNodeManager> activeNodeManagers,
            IEventMonitoredItem monitoredItem)
        {
            var dispatches = new List<NotificationDispatchLease>();
            lock (m_retiredGenerationNotificationsLock)
            {
                foreach (IAsyncNodeManager nodeManager in activeNodeManagers)
                {
                    AddActiveNotificationDispatch(dispatches, nodeManager);
                }
                foreach (RetiredGenerationNotifications notifications in
                    m_retiredGenerationNotifications)
                {
                    if (notifications.Enabled &&
                        notifications.SubscribedEventMonitoredItems.Any(candidate =>
                            ReferenceEquals(candidate, monitoredItem)) &&
                        !ContainsNodeManager(dispatches, notifications.NodeManager))
                    {
                        dispatches.Add(
                            CreateRetiredNotificationDispatch(notifications));
                    }
                }
            }
            return [.. dispatches];
        }

        internal NotificationDispatchLease[] GetConditionRefreshDispatches(
            IReadOnlyList<IAsyncNodeManager> activeNodeManagers,
            IList<IEventMonitoredItem> monitoredItems)
        {
            var dispatches = new List<NotificationDispatchLease>();
            IEventMonitoredItem[] currentItems = [.. monitoredItems];
            lock (m_retiredGenerationNotificationsLock)
            {
                foreach (IAsyncNodeManager nodeManager in activeNodeManagers)
                {
                    AddActiveNotificationDispatch(
                        dispatches,
                        nodeManager,
                        currentItems);
                }
                foreach (RetiredGenerationNotifications notifications in
                    m_retiredGenerationNotifications)
                {
                    if (!notifications.Enabled ||
                        ContainsNodeManager(dispatches, notifications.NodeManager))
                    {
                        continue;
                    }

                    IEventMonitoredItem[] retainedItems =
                    [
                        .. currentItems.Where(monitoredItem =>
                            notifications.SubscribedEventMonitoredItems.Any(candidate =>
                                ReferenceEquals(candidate, monitoredItem)) ||
                            ReferenceEquals(
                                monitoredItem.NodeManager,
                                notifications.NodeManager))
                    ];
                    if (retainedItems.Length > 0)
                    {
                        dispatches.Add(
                            CreateRetiredNotificationDispatch(
                                notifications,
                                retainedItems));
                    }
                }
            }
            return [.. dispatches];
        }

        internal NotificationDispatchLease[] GetAllEventUnsubscribeDispatches(
            IEventMonitoredItem monitoredItem)
        {
            IAsyncNodeManager[] activeNodeManagers = [.. m_nodeManagers];
            var dispatches = new List<NotificationDispatchLease>();
            lock (m_retiredGenerationNotificationsLock)
            {
                foreach (IAsyncNodeManager nodeManager in activeNodeManagers)
                {
                    AddActiveNotificationDispatch(dispatches, nodeManager);
                }
                foreach (RetiredGenerationNotifications notifications in
                    m_retiredGenerationNotifications)
                {
                    if (notifications.AcceptEventDeletes &&
                        notifications.SubscribedEventMonitoredItems.Any(candidate =>
                            ReferenceEquals(candidate, monitoredItem)) &&
                        !ContainsNodeManager(dispatches, notifications.NodeManager))
                    {
                        dispatches.Add(
                            CreateRetiredNotificationDispatch(notifications));
                    }
                }
            }
            return [.. dispatches];
        }

        internal void CompleteRetiredAllEventUnsubscribe(
            IEventMonitoredItem monitoredItem,
            IReadOnlyList<NotificationDispatchLease> dispatches)
        {
            lock (m_retiredGenerationNotificationsLock)
            {
                foreach (NotificationDispatchLease dispatch in dispatches)
                {
                    RetiredGenerationNotifications? retired =
                        dispatch.Notifications;
                    if (retired is null)
                    {
                        continue;
                    }
                    if (m_retiredGenerationNotifications.Contains(retired))
                    {
                        retired.SubscribedEventMonitoredItems.RemoveAll(candidate =>
                            ReferenceEquals(candidate, monitoredItem));
                    }
                }
            }
        }

        internal void CompleteRetiredAllEventUnsubscribe(
            IEventMonitoredItem monitoredItem,
            RetiredGenerationNotifications notifications)
        {
            lock (m_retiredGenerationNotificationsLock)
            {
                if (m_retiredGenerationNotifications.Contains(notifications))
                {
                    notifications.SubscribedEventMonitoredItems.RemoveAll(candidate =>
                        ReferenceEquals(candidate, monitoredItem));
                }
            }
        }

        private NotificationDispatchLease CreateRetiredNotificationDispatch(
            RetiredGenerationNotifications notifications,
            IEventMonitoredItem[]? monitoredItems = null)
        {
            return CreateNotificationDispatch(
                notifications.NodeManager,
                notifications.DispatchState,
                monitoredItems);
        }

        private NotificationDispatchLease? TryCreateActiveNotificationDispatch(
            IAsyncNodeManager nodeManager,
            IEventMonitoredItem[]? monitoredItems = null)
        {
            if (m_retiredGenerationNotifications.Any(candidate =>
                ReferenceEquals(candidate.NodeManager, nodeManager)))
            {
                return null;
            }
            NotificationDispatchState dispatchState =
                GetOrCreateNotificationDispatchState(nodeManager);
            return dispatchState.Enabled
                ? CreateNotificationDispatch(
                    nodeManager,
                    dispatchState,
                    monitoredItems)
                : null;
        }

        private void AddActiveNotificationDispatch(
            List<NotificationDispatchLease> dispatches,
            IAsyncNodeManager nodeManager,
            IEventMonitoredItem[]? monitoredItems = null)
        {
            NotificationDispatchLease? dispatch =
                TryCreateActiveNotificationDispatch(nodeManager, monitoredItems);
            try
            {
                if (dispatch is not null)
                {
                    dispatches.Add(dispatch);
                    dispatch = null;
                }
            }
            finally
            {
                dispatch?.Dispose();
            }
        }

        private NotificationDispatchLease CreateNotificationDispatch(
            IAsyncNodeManager nodeManager,
            NotificationDispatchState dispatchState,
            IEventMonitoredItem[]? monitoredItems)
        {
            dispatchState.ActiveDispatches++;
            return new NotificationDispatchLease(
                this,
                nodeManager,
                dispatchState,
                monitoredItems ?? []);
        }

        private static bool ContainsNodeManager(
            IReadOnlyList<NotificationDispatchLease> dispatches,
            IAsyncNodeManager nodeManager)
        {
            return dispatches.Any(dispatch =>
                ReferenceEquals(dispatch.NodeManager, nodeManager));
        }

        private void ReleaseNotificationDispatch(
            NotificationDispatchState dispatchState)
        {
            TaskCompletionSource<bool>? dispatchesDrained = null;
            lock (m_retiredGenerationNotificationsLock)
            {
                Debug.Assert(dispatchState.ActiveDispatches > 0);
                if (dispatchState.ActiveDispatches <= 0)
                {
                    return;
                }

                if (--dispatchState.ActiveDispatches == 0)
                {
                    dispatchesDrained = dispatchState.DispatchesDrained;
                    dispatchState.DispatchesDrained = null;
                    if (dispatchState.Enabled &&
                        !m_retiredGenerationNotifications.Any(notifications =>
                            ReferenceEquals(
                                notifications.DispatchState,
                                dispatchState)))
                    {
                        m_notificationDispatchStates.Remove(dispatchState);
                    }
                }
            }
            dispatchesDrained?.TrySetResult(true);
        }

        private NotificationDispatchState GetOrCreateNotificationDispatchState(
            IAsyncNodeManager nodeManager)
        {
            m_notificationDispatchStates.RemoveAll(candidate =>
                !candidate.IsAlive);
            NotificationDispatchState? dispatchState =
                m_notificationDispatchStates.FirstOrDefault(candidate =>
                    candidate.References(nodeManager));
            if (dispatchState is null)
            {
                dispatchState = new NotificationDispatchState(nodeManager);
                m_notificationDispatchStates.Add(dispatchState);
            }
            return dispatchState;
        }

        internal static void DisposeNotificationDispatches(
            IReadOnlyList<NotificationDispatchLease> dispatches)
        {
            foreach (NotificationDispatchLease dispatch in dispatches)
            {
                dispatch.Dispose();
            }
        }

        /// <inheritdoc/>
        void ISyncNodeManagerMonitoredItemRecovery.RecoverDetachedMonitoredItems(
            IAsyncNodeManager nodeManager,
            IReadOnlyCollection<NodeId> nodeIds)
        {
            if (!Server.IsRunning)
            {
                return;
            }

            IAsyncNodeManager? visibleNodeManager = GetVisibleNodeManager(nodeManager);
            if (visibleNodeManager is not INodeManagerMonitoredItemLifecycle nodeManagerLifecycle)
            {
                return;
            }

            var failures = new List<Exception>();
            foreach (ISubscription subscription in Server.SubscriptionManager.GetSubscriptions())
            {
                if (subscription is not ISubscriptionMonitoredItemLifecycle lifecycle)
                {
                    continue;
                }

                foreach (IMonitoredItem monitoredItem in lifecycle.GetRecoverableMonitoredItemsSnapshot(nodeIds))
                {
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

                        ServiceResult detachResult = CompleteInMemory(
                            currentLifecycle.DetachMonitoredItemAsync(monitoredItem));
                        if (ServiceResult.IsBad(detachResult))
                        {
                            failures.Add(new ServiceResultException(detachResult));
                            continue;
                        }
                    }

                    ServiceResult attachResult = CompleteInMemory(
                        nodeManagerLifecycle.AttachMonitoredItemAsync(monitoredItem));
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
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "One or more monitored items could not be recovered.",
                    failures);
            }
        }

        /// <inheritdoc/>
        async ValueTask IDynamicNodeManagerHost.RecoverDetachedMonitoredItemsAsync(
            IAsyncNodeManager nodeManager,
            IReadOnlyCollection<NodeId>? nodeIds,
            CancellationToken cancellationToken)
        {
            if (!Server.IsRunning)
            {
                return;
            }

            var monitoredItems = new List<IMonitoredItem>();
            foreach (ISubscription subscription in Server.SubscriptionManager.GetSubscriptions())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (subscription is not ISubscriptionMonitoredItemLifecycle lifecycle)
                {
                    continue;
                }

                monitoredItems.AddRange(
                    lifecycle.GetRecoverableMonitoredItemsSnapshot(nodeIds));
            }

            await RecoverMonitoredItemsAsync(
                nodeManager,
                monitoredItems,
                cancellationToken).ConfigureAwait(false);
        }

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

        /// <summary>
        /// Consumes a MonitoredItem lifecycle operation that the NodeManager completed in memory.
        /// The synchronous recovery path runs inside <c>AddPredefinedNode</c>, where attaching and
        /// detaching never suspends, so the result is already available and no blocking wait is
        /// introduced.
        /// </summary>
        /// <param name="operation">The operation the NodeManager started.</param>
        /// <returns>The result of the operation.</returns>
        /// <exception cref="InvalidOperationException">
        /// The NodeManager suspended an operation that has to complete in memory.
        /// </exception>
        private static ServiceResult CompleteInMemory(ValueTask<ServiceResult> operation)
        {
            if (!operation.IsCompleted)
            {
                throw new InvalidOperationException(
                    "A NodeManager must complete MonitoredItem recovery without suspending when " +
                    "a Node is added through the synchronous AddPredefinedNode path.");
            }
            return operation.Result;
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
            foreach (IAsyncNodeManager candidate in m_nodeManagers)
            {
                if (m_nodeManagers.IsVisible(candidate) &&
                    (ReferenceEquals(candidate, nodeManager) ||
                        ReferenceEquals(
                            candidate.SyncNodeManager,
                            nodeManager.SyncNodeManager)))
                {
                    return candidate;
                }
            }
            return null;
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

            // look up the namespace uri.
            int index = Server.NamespaceUris.GetIndex(namespaceUri);

            if (index < 0)
            {
                index = Server.NamespaceUris.Append(namespaceUri);
            }

            IAsyncNodeManager? preparingNodeManager =
                GetPreparingNodeManager(nodeManager);
            m_nodeManagers.RegisterNamespace(
                index,
                preparingNodeManager ?? nodeManager,
                visible: preparingNodeManager is null);
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

        /// <summary>
        /// Adds the references to the target.
        /// </summary>
        [Obsolete("Use AddReferencesAsync instead.")]
        public virtual void AddReferences(NodeId sourceId, IList<IReference> references)
        {
            AddReferencesAsync(sourceId, references).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public virtual async ValueTask AddReferencesAsync(NodeId sourceId,
            IList<IReference> references,
            CancellationToken cancellationToken = default)
        {
            // find source node.
            (object? sourceHandle, IAsyncNodeManager? nodeManager) = await GetManagerHandleAsync(sourceId, cancellationToken)
                .ConfigureAwait(false);
            if (sourceHandle == null)
            {
                return;
            }

            var map = new Dictionary<NodeId, IList<IReference>> { { sourceId, references } };
            await nodeManager!.AddReferencesAsync(map, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes the references to the target.
        /// </summary>
        [Obsolete("Use DeleteReferencesAsync")]
        public virtual void DeleteReferences(NodeId targetId, IList<IReference> references)
        {
            DeleteReferencesAsync(targetId, references).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public virtual async ValueTask DeleteReferencesAsync(NodeId targetId,
            IList<IReference> references,
            CancellationToken cancellationToken = default)
        {
            foreach (ReferenceNode reference in references.OfType<ReferenceNode>())
            {
                var sourceId = ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris);

                // find source node.
                (object? sourceHandle, IAsyncNodeManager? nodeManager) = await GetManagerHandleAsync(sourceId, cancellationToken)
                .ConfigureAwait(false);

                if (sourceHandle == null)
                {
                    continue;
                }

                // delete the reference.
                await nodeManager!.DeleteReferenceAsync(
                        sourceHandle,
                        reference.ReferenceTypeId,
                        !reference.IsInverse,
                        targetId,
                        false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        [Obsolete("Use RemoveReferencesAsync instead.")]
        public void RemoveReferences(List<LocalReference> referencesToRemove)
        {
            RemoveReferencesAsync(referencesToRemove).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public async ValueTask RemoveReferencesAsync(List<LocalReference> referencesToRemove, CancellationToken cancellationToken = default)
        {
            for (int ii = 0; ii < referencesToRemove.Count; ii++)
            {
                LocalReference reference = referencesToRemove[ii];

                // find source node.
                (object? sourceHandle, IAsyncNodeManager? nodeManager) = await GetManagerHandleAsync(
                        reference.SourceId,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (sourceHandle == null)
                {
                    continue;
                }

                // delete the reference.
                await nodeManager!.DeleteReferenceAsync(
                        sourceHandle,
                        reference.ReferenceTypeId,
                        reference.IsInverse,
                        reference.TargetId,
                        false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<(ArrayOf<AddNodesResult> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            AddNodesAsync(
                OperationContext context,
                ArrayOf<AddNodesItem> nodesToAdd,
                CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var results = new AddNodesResult[nodesToAdd.Count];
                var diagnosticInfos = new DiagnosticInfo[nodesToAdd.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < nodesToAdd.Count; ii++)
                {
                    AddNodesItem item = nodesToAdd[ii];
                    (ServiceResult result, NodeId addedNodeId) = await DispatchAddNodeAsync(
                        context,
                        item,
                        cancellationToken).ConfigureAwait(false);

                    results[ii] = new AddNodesResult
                    {
                        StatusCode = result.StatusCode,
                        AddedNodeId = addedNodeId
                    };

                    if (ServiceResult.IsBad(result) &&
                        (context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            result,
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return (results.ToArrayOf(), anyDiagnostics ? diagnosticInfos.ToArrayOf() : default);
            }
            finally
            {
                m_dynamicMutationSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            DeleteNodesAsync(
                OperationContext context,
                ArrayOf<DeleteNodesItem> nodesToDelete,
                CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            await m_dynamicMutationSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var results = new StatusCode[nodesToDelete.Count];
                var diagnosticInfos = new DiagnosticInfo[nodesToDelete.Count];
                bool anyDiagnostics = false;

                for (int ii = 0; ii < nodesToDelete.Count; ii++)
                {
                    DeleteNodesItem item = nodesToDelete[ii];
                    ServiceResult result = await DispatchDeleteNodeAsync(
                        context,
                        item,
                        cancellationToken).ConfigureAwait(false);

                    results[ii] = result.StatusCode;

                    if (ServiceResult.IsBad(result) &&
                        (context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        anyDiagnostics = true;
                        diagnosticInfos[ii] = new DiagnosticInfo(
                            result,
                            context.DiagnosticsMask,
                            false,
                            context.StringTable,
                            m_logger);
                    }
                }

                return (results.ToArrayOf(), anyDiagnostics ? diagnosticInfos.ToArrayOf() : default);
            }
            finally
            {
                m_dynamicMutationSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            AddReferencesAsync(
                OperationContext context,
                ArrayOf<AddReferencesItem> referencesToAdd,
                CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var results = new StatusCode[referencesToAdd.Count];
            var diagnosticInfos = new DiagnosticInfo[referencesToAdd.Count];
            bool anyDiagnostics = false;

            for (int ii = 0; ii < referencesToAdd.Count; ii++)
            {
                AddReferencesItem item = referencesToAdd[ii];
                ServiceResult result = await DispatchAddReferenceAsync(
                    context,
                    item,
                    cancellationToken).ConfigureAwait(false);

                results[ii] = result.StatusCode;

                if (ServiceResult.IsBad(result) &&
                    (context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                {
                    anyDiagnostics = true;
                    diagnosticInfos[ii] = new DiagnosticInfo(
                        result,
                        context.DiagnosticsMask,
                        false,
                        context.StringTable,
                        m_logger);
                }
            }

            return (results.ToArrayOf(), anyDiagnostics ? diagnosticInfos.ToArrayOf() : default);
        }

        /// <inheritdoc/>
        public virtual async ValueTask<(ArrayOf<StatusCode> results, ArrayOf<DiagnosticInfo> diagnosticInfos)>
            DeleteReferencesAsync(
                OperationContext context,
                ArrayOf<DeleteReferencesItem> referencesToDelete,
                CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var results = new StatusCode[referencesToDelete.Count];
            var diagnosticInfos = new DiagnosticInfo[referencesToDelete.Count];
            bool anyDiagnostics = false;

            for (int ii = 0; ii < referencesToDelete.Count; ii++)
            {
                DeleteReferencesItem item = referencesToDelete[ii];
                ServiceResult result = await DispatchDeleteReferenceAsync(
                    context,
                    item,
                    cancellationToken).ConfigureAwait(false);

                results[ii] = result.StatusCode;

                if (ServiceResult.IsBad(result) &&
                    (context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                {
                    anyDiagnostics = true;
                    diagnosticInfos[ii] = new DiagnosticInfo(
                        result,
                        context.DiagnosticsMask,
                        false,
                        context.StringTable,
                        m_logger);
                }
            }

            return (results.ToArrayOf(), anyDiagnostics ? diagnosticInfos.ToArrayOf() : default);
        }

        private async ValueTask<(ServiceResult result, NodeId addedNodeId)> DispatchAddNodeAsync(
            OperationContext context,
            AddNodesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null)
            {
                return (new ServiceResult(StatusCodes.BadNothingToDo), NodeId.Null);
            }

            if (item.BrowseName.IsNull)
            {
                return (new ServiceResult(StatusCodes.BadBrowseNameInvalid), NodeId.Null);
            }

            if (item.ParentNodeId.IsNull)
            {
                return (new ServiceResult(StatusCodes.BadParentNodeIdInvalid), NodeId.Null);
            }

            if (item.ReferenceTypeId.IsNull ||
                !Server.TypeTree.IsKnown(item.ReferenceTypeId))
            {
                return (new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid), NodeId.Null);
            }

            if (!Server.TypeTree.IsTypeOf(
                    item.ReferenceTypeId,
                    ReferenceTypeIds.HierarchicalReferences))
            {
                return (new ServiceResult(StatusCodes.BadReferenceNotAllowed), NodeId.Null);
            }

            var parentNodeId = ExpandedNodeId.ToNodeId(item.ParentNodeId, Server.NamespaceUris);
            if (parentNodeId.IsNull)
            {
                return (new ServiceResult(StatusCodes.BadParentNodeIdInvalid), NodeId.Null);
            }

            bool hasRequestedNodeId = !item.RequestedNewNodeId.IsNull;
            ushort targetNamespaceIndex;
            IAsyncNodeManager? nodeManagement = null;
            if (hasRequestedNodeId)
            {
                var requestedNodeId = ExpandedNodeId.ToNodeId(
                    item.RequestedNewNodeId, Server.NamespaceUris);
                if (requestedNodeId.IsNull)
                {
                    return (new ServiceResult(StatusCodes.BadNodeIdRejected), NodeId.Null);
                }

                targetNamespaceIndex = requestedNodeId.NamespaceIndex;

                if (!NamespaceManagers.TryGetValue(
                        targetNamespaceIndex,
                        out IReadOnlyList<IAsyncNodeManager>? namespaceOwners) ||
                    namespaceOwners.Count == 0)
                {
                    return (new ServiceResult(StatusCodes.BadNodeIdRejected), NodeId.Null);
                }

                nodeManagement = FindNodeManagementOwner(targetNamespaceIndex);
                if (nodeManagement == null)
                {
                    return (new ServiceResult(StatusCodes.BadNodeIdRejected), NodeId.Null);
                }
            }
            else
            {
                targetNamespaceIndex = item.BrowseName.NamespaceIndex;
            }

            (object? parentHandle, IAsyncNodeManager? parentOwner) =
                await GetManagerHandleAsync(parentNodeId, cancellationToken).ConfigureAwait(false);
            if (parentHandle == null || parentOwner == null)
            {
                return (new ServiceResult(StatusCodes.BadParentNodeIdInvalid), NodeId.Null);
            }

            NodeMetadata? parentMetadata;
            try
            {
                parentMetadata = await parentOwner.GetNodeMetadataAsync(
                    context,
                    parentHandle,
                    BrowseResultMask.NodeClass,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return (new ServiceResult(ex), NodeId.Null);
            }

            if (parentMetadata == null)
            {
                return (new ServiceResult(StatusCodes.BadParentNodeIdInvalid), NodeId.Null);
            }

            if (!IsReferenceAllowedForNodeClasses(
                    item.ReferenceTypeId,
                    parentMetadata.NodeClass,
                    item.NodeClass))
            {
                return (new ServiceResult(StatusCodes.BadReferenceNotAllowed), NodeId.Null);
            }

            if (nodeManagement == null)
            {
                if (!NamespaceManagers.TryGetValue(
                        targetNamespaceIndex,
                        out IReadOnlyList<IAsyncNodeManager>? namespaceOwners) ||
                    namespaceOwners.Count == 0)
                {
                    return (new ServiceResult(StatusCodes.BadNodeIdRejected), NodeId.Null);
                }

                nodeManagement = FindNodeManagementOwner(targetNamespaceIndex);
                if (nodeManagement == null)
                {
                    return (new ServiceResult(StatusCodes.BadUserAccessDenied), NodeId.Null);
                }
            }

            ServiceResult permissionResult = await ValidateAddNodeNamespacePermissionAsync(
                context,
                targetNamespaceIndex,
                cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(permissionResult))
            {
                return (permissionResult, NodeId.Null);
            }

            permissionResult = await ValidatePermissionsAsync(
                context,
                parentOwner,
                parentHandle,
                PermissionType.AddReference,
                null,
                permissionsOnly: true,
                cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(permissionResult))
            {
                return (permissionResult, NodeId.Null);
            }

            try
            {
                return await nodeManagement.AddNodeAsync(context, item, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return (new ServiceResult(ex), NodeId.Null);
            }
        }

        private async ValueTask<ServiceResult> DispatchDeleteNodeAsync(
            OperationContext context,
            DeleteNodesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null)
            {
                return new ServiceResult(StatusCodes.BadNothingToDo);
            }

            if (item.NodeId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadNodeIdInvalid);
            }

            (object? handle, IAsyncNodeManager? owner) =
                await GetManagerHandleAsync(item.NodeId, cancellationToken).ConfigureAwait(false);
            if (handle == null || owner == null)
            {
                return new ServiceResult(StatusCodes.BadNodeIdUnknown);
            }

            if (!owner.AllowNodeManagement)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }

            ServiceResult permissionResult = await ValidatePermissionsAsync(
                context,
                owner,
                handle,
                PermissionType.DeleteNode,
                null,
                permissionsOnly: true,
                cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(permissionResult))
            {
                return permissionResult;
            }

            try
            {
                return await owner.DeleteNodeAsync(context, item, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return new ServiceResult(ex);
            }
        }

        private async ValueTask<ServiceResult> DispatchAddReferenceAsync(
            OperationContext context,
            AddReferencesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null)
            {
                return new ServiceResult(StatusCodes.BadNothingToDo);
            }

            if (item.SourceNodeId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }

            if (item.TargetNodeId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
            }

            if (item.ReferenceTypeId.IsNull ||
                !Server.TypeTree.IsKnown(item.ReferenceTypeId))
            {
                return new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid);
            }

            (object? sourceHandle, IAsyncNodeManager? sourceOwner) =
                await GetManagerHandleAsync(item.SourceNodeId, cancellationToken).ConfigureAwait(false);
            if (sourceHandle == null || sourceOwner == null)
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }

            if (!sourceOwner.AllowNodeManagement)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }

            (ServiceResult permissionResult, NodeMetadata? sourceMetadata) =
                await m_serviceDispatch.ValidatePermissionsAndGetMetadataAsync(
                    context,
                    sourceOwner,
                    sourceHandle,
                    PermissionType.AddReference,
                    null,
                    permissionsOnly: true,
                    metadataRequired: true,
                    cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(permissionResult))
            {
                return permissionResult;
            }

            IAsyncNodeManager? targetOwner = null;
            NodeMetadata? targetMetadata = null;

            if (TryGetExplicitLocalTargetNodeId(
                item.TargetServerUri,
                item.TargetNodeId,
                out NodeId targetNodeId))
            {
                object? targetHandle;
                (targetHandle, targetOwner) = await GetManagerHandleAsync(
                    targetNodeId,
                    cancellationToken).ConfigureAwait(false);
                if (targetHandle == null || targetOwner == null)
                {
                    return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
                }

                if (!ReferenceEquals(targetOwner, sourceOwner) &&
                    !targetOwner.AllowNodeManagement)
                {
                    return new ServiceResult(StatusCodes.BadUserAccessDenied);
                }

                (permissionResult, targetMetadata) =
                    await m_serviceDispatch.ValidatePermissionsAndGetMetadataAsync(
                        context,
                        targetOwner,
                        targetHandle,
                        PermissionType.AddReference,
                        null,
                        permissionsOnly: true,
                        metadataRequired: true,
                        cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(permissionResult))
                {
                    return permissionResult;
                }
            }

            bool crossManagerTarget =
                targetOwner != null &&
                !ReferenceEquals(targetOwner, sourceOwner);
            if (crossManagerTarget &&
                (sourceMetadata == null || sourceMetadata.NodeClass == NodeClass.Unspecified))
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }
            if (crossManagerTarget &&
                (targetMetadata == null || targetMetadata.NodeClass == NodeClass.Unspecified))
            {
                return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
            }

            ServiceResult sourceResult;
            try
            {
                sourceResult = await sourceOwner.AddReferenceAsync(context, item, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return new ServiceResult(ex);
            }

            if (ServiceResult.IsBad(sourceResult))
            {
                return sourceResult;
            }

            // Write the complementary edge into the target's owning manager when the
            // target is explicitly local. Roll back the source edge if the target mutation fails.
            if (crossManagerTarget)
            {
                var inverseItem = new AddReferencesItem
                {
                    SourceNodeId = targetNodeId,
                    ReferenceTypeId = item.ReferenceTypeId,
                    IsForward = !item.IsForward,
                    TargetServerUri = string.Empty,
                    TargetNodeId = item.SourceNodeId,
                    TargetNodeClass = sourceMetadata!.NodeClass
                };

                try
                {
                    ServiceResult inverseResult = await targetOwner!.AddReferenceAsync(
                        context, inverseItem, cancellationToken).ConfigureAwait(false);
                    if (ServiceResult.IsBad(inverseResult))
                    {
                        m_logger.AddReferencesFailedToMirrorInverseEdgeRefType(
                            item.ReferenceTypeId,
                            item.SourceNodeId,
                            item.TargetNodeId,
                            inverseResult.StatusCode);
                        await RollbackAddedReferenceAsync(
                            sourceOwner,
                            context,
                            item).ConfigureAwait(false);
                        return inverseResult;
                    }
                }
                catch (Exception ex)
                {
                    m_logger.AddReferencesFailedToMirrorInverseEdgeRefType2(
                        ex,
                        item.ReferenceTypeId,
                        item.SourceNodeId,
                        item.TargetNodeId);
                    await RollbackAddedReferenceAsync(
                        sourceOwner,
                        context,
                        item).ConfigureAwait(false);
                    if (ex is ServiceResultException serviceResultException)
                    {
                        return new ServiceResult(serviceResultException);
                    }

                    throw;
                }
            }

            return sourceResult;
        }

        private async ValueTask<ServiceResult> DispatchDeleteReferenceAsync(
            OperationContext context,
            DeleteReferencesItem item,
            CancellationToken cancellationToken)
        {
            if (item == null)
            {
                return new ServiceResult(StatusCodes.BadNothingToDo);
            }

            if (item.SourceNodeId.IsNull)
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }

            if (item.ReferenceTypeId.IsNull ||
                !Server.TypeTree.IsKnown(item.ReferenceTypeId))
            {
                return new ServiceResult(StatusCodes.BadReferenceTypeIdInvalid);
            }

            (object? sourceHandle, IAsyncNodeManager? sourceOwner) =
                await GetManagerHandleAsync(item.SourceNodeId, cancellationToken).ConfigureAwait(false);
            if (sourceHandle == null || sourceOwner == null)
            {
                return new ServiceResult(StatusCodes.BadSourceNodeIdInvalid);
            }

            if (!sourceOwner.AllowNodeManagement)
            {
                return new ServiceResult(StatusCodes.BadUserAccessDenied);
            }

            (ServiceResult permissionResult, _) =
                await m_serviceDispatch.ValidatePermissionsAndGetMetadataAsync(
                    context,
                    sourceOwner,
                    sourceHandle,
                    PermissionType.RemoveReference,
                    null,
                    permissionsOnly: true,
                    metadataRequired: true,
                    cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(permissionResult))
            {
                return permissionResult;
            }

            NodeId targetNodeId = NodeId.Null;
            IAsyncNodeManager? targetOwner = null;
            NodeMetadata? targetMetadata = null;
            bool explicitlyLocalTarget =
                item.DeleteBidirectional &&
                TryGetExplicitLocalTargetNodeId(
                    targetServerUri: null,
                    item.TargetNodeId,
                    out targetNodeId);
            if (explicitlyLocalTarget)
            {
                object? targetHandle;
                (targetHandle, targetOwner) = await GetManagerHandleAsync(
                    targetNodeId,
                    cancellationToken).ConfigureAwait(false);
                if (targetHandle == null || targetOwner == null)
                {
                    return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
                }

                if (!ReferenceEquals(targetOwner, sourceOwner) &&
                    !targetOwner.AllowNodeManagement)
                {
                    return new ServiceResult(StatusCodes.BadUserAccessDenied);
                }

                (permissionResult, targetMetadata) =
                    await m_serviceDispatch.ValidatePermissionsAndGetMetadataAsync(
                        context,
                        targetOwner,
                        targetHandle,
                        PermissionType.RemoveReference,
                        null,
                        permissionsOnly: true,
                        metadataRequired: true,
                        cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(permissionResult))
                {
                    return permissionResult;
                }
            }

            bool crossManagerTarget =
                targetOwner != null &&
                !ReferenceEquals(targetOwner, sourceOwner);
            if (crossManagerTarget &&
                (targetMetadata == null || targetMetadata.NodeClass == NodeClass.Unspecified))
            {
                return new ServiceResult(StatusCodes.BadTargetNodeIdInvalid);
            }

            DeleteReferencesItem sourceItem = item;
            if (item.DeleteBidirectional &&
                (!explicitlyLocalTarget || crossManagerTarget))
            {
                sourceItem = new DeleteReferencesItem
                {
                    SourceNodeId = item.SourceNodeId,
                    ReferenceTypeId = item.ReferenceTypeId,
                    IsForward = item.IsForward,
                    TargetNodeId = item.TargetNodeId,
                    DeleteBidirectional = false
                };
            }

            ServiceResult sourceResult;
            try
            {
                sourceResult = await sourceOwner.DeleteReferenceAsync(
                    context,
                    sourceItem,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return new ServiceResult(ex);
            }

            if (ServiceResult.IsBad(sourceResult))
            {
                return sourceResult;
            }

            if (!crossManagerTarget)
            {
                return sourceResult;
            }

            var inverseItem = new DeleteReferencesItem
            {
                SourceNodeId = targetNodeId,
                ReferenceTypeId = item.ReferenceTypeId,
                IsForward = !item.IsForward,
                TargetNodeId = item.SourceNodeId,
                DeleteBidirectional = false
            };

            try
            {
                ServiceResult inverseResult = await targetOwner!.DeleteReferenceAsync(
                    context, inverseItem, cancellationToken).ConfigureAwait(false);
                if (ServiceResult.IsBad(inverseResult))
                {
                    m_logger.DeleteReferencesFailedToMirrorInverseDeleteRefType(
                        item.ReferenceTypeId,
                        item.SourceNodeId,
                        item.TargetNodeId,
                        inverseResult.StatusCode);
                    await RollbackDeletedReferenceAsync(
                        sourceOwner,
                        context,
                        item,
                        targetMetadata!.NodeClass).ConfigureAwait(false);
                    return inverseResult;
                }
            }
            catch (Exception ex)
            {
                m_logger.DeleteReferencesFailedToMirrorInverseDeleteRefType2(
                    ex,
                    item.ReferenceTypeId,
                    item.SourceNodeId,
                    item.TargetNodeId);
                await RollbackDeletedReferenceAsync(
                    sourceOwner,
                    context,
                    item,
                    targetMetadata!.NodeClass).ConfigureAwait(false);
                if (ex is ServiceResultException serviceResultException)
                {
                    return new ServiceResult(serviceResultException);
                }

                throw;
            }

            return sourceResult;
        }

        private bool TryGetExplicitLocalTargetNodeId(
            string? targetServerUri,
            ExpandedNodeId targetNodeId,
            out NodeId localNodeId)
        {
            if (!string.IsNullOrEmpty(targetServerUri) ||
                targetNodeId.ServerIndex != 0)
            {
                localNodeId = NodeId.Null;
                return false;
            }

            localNodeId = ExpandedNodeId.ToNodeId(targetNodeId, Server.NamespaceUris);
            return !localNodeId.IsNull;
        }

        private async ValueTask RollbackAddedReferenceAsync(
            IAsyncNodeManager sourceOwner,
            OperationContext context,
            AddReferencesItem item)
        {
            var rollbackItem = new DeleteReferencesItem
            {
                SourceNodeId = item.SourceNodeId,
                ReferenceTypeId = item.ReferenceTypeId,
                IsForward = item.IsForward,
                TargetNodeId = item.TargetNodeId,
                DeleteBidirectional = false
            };

            using var cleanupCts = new CancellationTokenSource(
                s_nodeManagementCompensationTimeout);
            try
            {
                ServiceResult rollbackResult = await sourceOwner.DeleteReferenceAsync(
                    context,
                    rollbackItem,
                    cleanupCts.Token).ConfigureAwait(false);
                if (ServiceResult.IsBad(rollbackResult))
                {
                    m_logger.AddReferencesRollbackFailed(
                        item.ReferenceTypeId,
                        item.SourceNodeId,
                        item.TargetNodeId,
                        rollbackResult.StatusCode);
                }
            }
            catch (Exception ex)
            {
                m_logger.AddReferencesRollbackFailed2(
                    ex,
                    item.ReferenceTypeId,
                    item.SourceNodeId,
                    item.TargetNodeId);
            }
        }

        private async ValueTask RollbackDeletedReferenceAsync(
            IAsyncNodeManager sourceOwner,
            OperationContext context,
            DeleteReferencesItem item,
            NodeClass targetNodeClass)
        {
            var rollbackItem = new AddReferencesItem
            {
                SourceNodeId = item.SourceNodeId,
                ReferenceTypeId = item.ReferenceTypeId,
                IsForward = item.IsForward,
                TargetServerUri = string.Empty,
                TargetNodeId = item.TargetNodeId,
                TargetNodeClass = targetNodeClass
            };

            using var cleanupCts = new CancellationTokenSource(
                s_nodeManagementCompensationTimeout);
            try
            {
                ServiceResult rollbackResult = await sourceOwner.AddReferenceAsync(
                    context,
                    rollbackItem,
                    cleanupCts.Token).ConfigureAwait(false);
                if (ServiceResult.IsBad(rollbackResult))
                {
                    m_logger.DeleteReferencesRollbackFailed(
                        item.ReferenceTypeId,
                        item.SourceNodeId,
                        item.TargetNodeId,
                        rollbackResult.StatusCode);
                }
            }
            catch (Exception ex)
            {
                m_logger.DeleteReferencesRollbackFailed2(
                    ex,
                    item.ReferenceTypeId,
                    item.SourceNodeId,
                    item.TargetNodeId);
            }
        }

        private async ValueTask<ServiceResult> ValidateAddNodeNamespacePermissionAsync(
            OperationContext context,
            ushort namespaceIndex,
            CancellationToken cancellationToken)
        {
            if (context.Session == null || ConfigurationNodeManager == null)
            {
                return StatusCodes.Good;
            }

            try
            {
                NamespaceMetadataState? namespaceMetadata =
                    await ConfigurationNodeManager.GetNamespaceMetadataStateAsync(
                        namespaceIndex,
                        cancellationToken).ConfigureAwait(false);
                if (namespaceMetadata == null)
                {
                    return StatusCodes.Good;
                }

                var metadata = new NodeMetadata(
                    namespaceMetadata,
                    new NodeId(0u, namespaceIndex));

                if (namespaceMetadata.DefaultAccessRestrictions != null)
                {
                    metadata.DefaultAccessRestrictions =
                        (AccessRestrictionType)namespaceMetadata.DefaultAccessRestrictions.Value;
                }

                if (namespaceMetadata.DefaultRolePermissions != null)
                {
                    metadata.DefaultRolePermissions =
                        namespaceMetadata.DefaultRolePermissions.Value;
                }

                if (namespaceMetadata.DefaultUserRolePermissions != null)
                {
                    metadata.DefaultUserRolePermissions =
                        namespaceMetadata.DefaultUserRolePermissions.Value;
                }

                return m_serviceDispatch.ValidatePermissionMetadata(context, metadata, PermissionType.AddNode);
            }
            catch (ServiceResultException ex)
            {
                return new ServiceResult(ex);
            }
        }

        /// <summary>
        /// Checks the NodeClass constraints defined in OPC UA Part 3 for concrete
        /// standard hierarchical ReferenceTypes.
        /// </summary>
        private static bool IsReferenceAllowedForNodeClasses(
            NodeId referenceTypeId,
            NodeClass parentNodeClass,
            NodeClass newNodeClass)
        {
            if (newNodeClass == NodeClass.Unspecified)
            {
                return true;
            }

            if (referenceTypeId == ReferenceTypeIds.HasSubtype)
            {
                return parentNodeClass == newNodeClass &&
                    parentNodeClass is NodeClass.ObjectType or
                        NodeClass.VariableType or
                        NodeClass.DataType or
                        NodeClass.ReferenceType;
            }

            if (referenceTypeId == ReferenceTypeIds.Organizes)
            {
                return parentNodeClass is NodeClass.Object or NodeClass.ObjectType or NodeClass.View;
            }

            if (referenceTypeId == ReferenceTypeIds.HasComponent)
            {
                return (parentNodeClass is NodeClass.Object or
                            NodeClass.Variable or
                            NodeClass.ObjectType or
                            NodeClass.VariableType) &&
                    newNodeClass is NodeClass.Object or NodeClass.Variable or NodeClass.Method;
            }

            if (referenceTypeId == ReferenceTypeIds.HasProperty)
            {
                return newNodeClass == NodeClass.Variable;
            }

            return true;
        }

        /// <summary>
        /// Returns the first NodeManager registered against the given namespace
        /// index that has opted in to NodeManagement, or <c>null</c> when none has.
        /// </summary>
        private IAsyncNodeManager? FindNodeManagementOwner(ushort namespaceIndex)
        {
            if (!NamespaceManagers.TryGetValue(namespaceIndex, out IReadOnlyList<IAsyncNodeManager>? nodeManagers))
            {
                return null;
            }

            for (int ii = 0; ii < nodeManagers.Count; ii++)
            {
                if (nodeManagers[ii].AllowNodeManagement)
                {
                    return nodeManagers[ii];
                }
            }
            return null;
        }

        ValueTask<IMonitoredItemTransferTransaction>
            IMonitoredItemTransferCoordinator.PrepareMonitoredItemsTransferAsync(
                OperationContext destinationContext,
                bool sendInitialValues,
                IList<IMonitoredItem> monitoredItems,
                IList<ServiceResult> errors,
                MonitoredItemTransferOptions transferOptions,
                CancellationToken cancellationToken)
        {
            return m_serviceDispatch.PrepareMonitoredItemsTransferAsync(
                destinationContext,
                sendInitialValues,
                monitoredItems,
                errors,
                transferOptions,
                cancellationToken);
        }

        private static void ValidatePreparedNodeManager(
            PreparedNodeManager prepared,
            bool allowPublished = false)
        {
            if (prepared is null)
            {
                throw new ArgumentNullException(nameof(prepared));
            }
            if (prepared.Published && !allowPublished)
            {
                throw new InvalidOperationException(
                    "The prepared NodeManager has already been published.");
            }
        }

        private async ValueTask CommitAddAsync(
            PreparedNodeManager prepared)
        {
            bool routeAdded = false;
            try
            {
                m_nodeManagers.Add(
                    prepared.NodeManager,
                    ResolveNamespaceIndexes(prepared.NodeManager));
                routeAdded = true;
                await AddExternalReferencesAsync(
                    prepared.ExternalReferences,
                    prepared.NodeManager,
                    CancellationToken.None).ConfigureAwait(false);
                m_dynamicExternalReferences.Add(
                    prepared.NodeManager,
                    prepared.ExternalReferences);
                await ReplayRetainedExternalReferencesAsync(
                    prepared.NodeManager,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                var failures = new List<Exception> { ex };
                if (routeAdded)
                {
                    bool referencesRemoved = false;
                    try
                    {
                        await RemoveExternalReferencesAsync(
                            prepared.ExternalReferences,
                            CancellationToken.None).ConfigureAwait(false);
                        referencesRemoved = true;
                    }
                    catch (Exception rollbackException) when (
                        rollbackException is not OutOfMemoryException)
                    {
                        failures.Add(rollbackException);
                    }

                    if (referencesRemoved)
                    {
                        try
                        {
                            m_nodeManagers.Remove(prepared.NodeManager);
                        }
                        catch (Exception rollbackException) when (
                            rollbackException is not OutOfMemoryException)
                        {
                            failures.Add(rollbackException);
                        }
                    }

                    if (m_nodeManagers.IsVisible(prepared.NodeManager))
                    {
                        try
                        {
                            await AddExternalReferencesAsync(
                                prepared.ExternalReferences,
                                prepared.NodeManager,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception rollbackException) when (
                            rollbackException is not OutOfMemoryException)
                        {
                            failures.Add(rollbackException);
                        }
                        m_dynamicExternalReferences[prepared.NodeManager] =
                            prepared.ExternalReferences;
                        RetainPreparedNodeManager(prepared);
                    }
                    else
                    {
                        // The NodeManager is out of the routing table again, so the
                        // references retained for it must go with it. Anything that
                        // throws after they were recorded - the replay of retained
                        // references does - would otherwise leave them behind, and a
                        // NodeManager present here but absent from the routing table
                        // breaks two things: PublishAsync refuses to register the
                        // instance ever again because it looks already registered,
                        // and every later add replays these dead references into the
                        // NodeManager being added.
                        m_dynamicExternalReferences.Remove(prepared.NodeManager);
                    }
                }
                if (failures.Count > 1)
                {
                    throw new AggregateException(
                        "NodeManager publication and rollback both failed.",
                        failures);
                }
                throw;
            }
        }

        private async ValueTask CommitReplacementAsync(
            PreparedNodeManager prepared)
        {
            IAsyncNodeManager current = prepared.ReplacedNodeManager!;
            Dictionary<NodeId, IList<IReference>> currentExternalReferences =
                prepared.ReplacedExternalReferences!;
            bool currentWasVisible = m_nodeManagers.IsVisible(current);
            bool currentReferenceMutationStarted = false;
            bool replacementReferenceMutationStarted = false;
            bool routeReplaced = false;
            bool retiredNotificationsRetained = false;
            try
            {
                currentReferenceMutationStarted = true;
                await RemoveExternalReferencesAsync(
                    currentExternalReferences,
                    CancellationToken.None).ConfigureAwait(false);
                if (prepared.RetainReplacedNotifications)
                {
                    RetainRetiredGenerationNotifications(current);
                    retiredNotificationsRetained = true;
                }
                m_nodeManagers.Replace(
                    current,
                    prepared.NodeManager,
                    ResolveNamespaceIndexes(prepared.NodeManager));
                routeReplaced = true;
                replacementReferenceMutationStarted = true;
                await AddExternalReferencesAsync(
                    prepared.ExternalReferences,
                    prepared.NodeManager,
                    CancellationToken.None).ConfigureAwait(false);
                m_dynamicExternalReferences.Add(
                    prepared.NodeManager,
                    prepared.ExternalReferences);
                m_dynamicExternalReferences.Remove(current);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                var failures = new List<Exception> { ex };
                bool replacementReferencesRemoved =
                    !replacementReferenceMutationStarted;
                if (replacementReferenceMutationStarted)
                {
                    try
                    {
                        await RemoveExternalReferencesAsync(
                            prepared.ExternalReferences,
                            CancellationToken.None).ConfigureAwait(false);
                        replacementReferencesRemoved = true;
                    }
                    catch (Exception rollbackException) when (
                        rollbackException is not OutOfMemoryException)
                    {
                        failures.Add(rollbackException);
                    }
                }
                bool currentRestored = !routeReplaced;
                if (routeReplaced &&
                    replacementReferencesRemoved)
                {
                    try
                    {
                        m_nodeManagers.Replace(
                            prepared.NodeManager,
                            current,
                            ResolveNamespaceIndexes(current),
                            replacementVisible: false);
                        currentRestored = true;
                    }
                    catch (Exception rollbackException) when (
                        rollbackException is not OutOfMemoryException)
                    {
                        failures.Add(rollbackException);
                    }
                }

                if (currentRestored)
                {
                    bool currentVisibilityRestored = !routeReplaced;
                    if (currentReferenceMutationStarted)
                    {
                        try
                        {
                            await AddExternalReferencesAsync(
                                currentExternalReferences,
                                current,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception rollbackException) when (
                            rollbackException is not OutOfMemoryException)
                        {
                            failures.Add(rollbackException);
                        }
                    }
                    m_dynamicExternalReferences.Remove(prepared.NodeManager);
                    m_dynamicExternalReferences[current] =
                        currentExternalReferences;
                    if (routeReplaced)
                    {
                        try
                        {
                            m_nodeManagers.SetVisible(
                                current,
                                currentWasVisible);
                            currentVisibilityRestored = true;
                        }
                        catch (Exception rollbackException) when (
                            rollbackException is not OutOfMemoryException)
                        {
                            failures.Add(rollbackException);
                        }
                    }
                    if (retiredNotificationsRetained &&
                        currentVisibilityRestored)
                    {
                        RemoveRetiredGenerationNotifications(current);
                    }
                }
                else
                {
                    try
                    {
                        await AddExternalReferencesAsync(
                            prepared.ExternalReferences,
                            prepared.NodeManager,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception rollbackException) when (
                        rollbackException is not OutOfMemoryException)
                    {
                        failures.Add(rollbackException);
                    }
                    m_dynamicExternalReferences.Remove(current);
                    m_dynamicExternalReferences[prepared.NodeManager] =
                        prepared.ExternalReferences;
                    RetainPreparedNodeManager(prepared);
                }

                if (failures.Count > 1)
                {
                    throw new AggregateException(
                        "NodeManager replacement and rollback both failed.",
                        failures);
                }
                throw;
            }
        }

        private void RetainPreparedNodeManager(
            PreparedNodeManager prepared)
        {
            prepared.Staged = false;
            prepared.Published = true;
            prepared.ReplacedNodeManager = null;
            prepared.ReplacedExternalReferences = null;
            SetPreparing(prepared.NodeManager, preparing: false);
        }

        private void EnsureNoActiveMonitoredItems(
            IAsyncNodeManager nodeManager)
        {
            foreach (ISubscription subscription in
                Server.SubscriptionManager.GetSubscriptions())
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
                    throw new InvalidOperationException(
                        "The NodeManager cannot be reloaded or removed while it owns monitored items.");
                }
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
            return
            [
                .. nodeManager.NamespaceUris
                    .Select(namespaceUri => (int)Server.NamespaceUris.GetIndexOrAppend(namespaceUri))
            ];
        }

        /// <summary>
        /// Applies the external references retained from startup and from every
        /// other dynamically registered NodeManager to a NodeManager that has
        /// just been added.
        /// </summary>
        /// <remarks>
        /// An external reference names a Node another NodeManager owns, and a
        /// reference to a Node that is not in the address space is dropped
        /// rather than queued. Startup avoids the problem by collecting every
        /// NodeManager's external references first and applying them all
        /// afterwards, so the order in which NodeManagers are created does not
        /// matter. A NodeManager added later has no such second phase: anything
        /// that referenced one of its Nodes before it existed lost that edge,
        /// leaving the two ends of the Reference disagreeing - the target Node
        /// browses to the source, the source does not list the target. Replaying
        /// the retained references closes that gap. Each NodeManager applies
        /// only the entries whose source Node it owns, so this is a no-op for
        /// everything the new NodeManager does not own.
        /// </remarks>
        /// <param name="added">The NodeManager that has just been added.</param>
        /// <param name="ct">The cancellation token.</param>
        private async ValueTask ReplayRetainedExternalReferencesAsync(
            IAsyncNodeManager added,
            CancellationToken ct)
        {
            if (m_startupExternalReferences is { Count: > 0 } startup)
            {
                await added.AddReferencesAsync(startup, ct).ConfigureAwait(false);
            }

            foreach (KeyValuePair<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>> entry
                in m_dynamicExternalReferences)
            {
                if (ReferenceEquals(entry.Key, added) || entry.Value.Count == 0)
                {
                    continue;
                }
                await added.AddReferencesAsync(entry.Value, ct).ConfigureAwait(false);
            }
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

        /// <inheritdoc/>
        public IReadOnlyList<IAsyncNodeManager> AsyncNodeManagers => [.. m_nodeManagers];

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

        private static readonly TimeSpan s_nodeManagementCompensationTimeout =
            TimeSpan.FromSeconds(5);

        internal sealed class NotificationDispatchLease : IDisposable
        {
            public NotificationDispatchLease(
                MasterNodeManager owner,
                IAsyncNodeManager nodeManager,
                NotificationDispatchState dispatchState,
                IEventMonitoredItem[] monitoredItems)
            {
                m_owner = owner;
                NodeManager = nodeManager;
                DispatchState = dispatchState;
                MonitoredItems = monitoredItems;
            }

            public IAsyncNodeManager NodeManager { get; }

            public NotificationDispatchState DispatchState { get; }

            public RetiredGenerationNotifications? Notifications =>
                DispatchState.Notifications;

            public IEventMonitoredItem[] MonitoredItems { get; }

            public void Dispose()
            {
                Interlocked.Exchange(ref m_owner, null)?
                    .ReleaseNotificationDispatch(DispatchState);
            }

            private MasterNodeManager? m_owner;
        }

        internal sealed class RetiredGenerationNotifications
        {
            public RetiredGenerationNotifications(
                IAsyncNodeManager nodeManager,
                NotificationDispatchState dispatchState,
                List<IEventMonitoredItem> eventMonitoredItems)
            {
                NodeManager = nodeManager;
                DispatchState = dispatchState;
                SubscribedEventMonitoredItems = eventMonitoredItems;
            }

            public IAsyncNodeManager NodeManager { get; }

            public NotificationDispatchState DispatchState { get; }

            public List<IEventMonitoredItem> SubscribedEventMonitoredItems { get; }

            public bool Enabled { get; set; } = true;

            public bool AcceptEventDeletes { get; set; } = true;

        }

        internal sealed class NotificationDispatchState
        {
            public NotificationDispatchState(IAsyncNodeManager nodeManager)
            {
                m_nodeManager = new WeakReference<IAsyncNodeManager>(nodeManager);
            }

            public bool IsAlive => m_nodeManager.TryGetTarget(out _);

            public bool References(IAsyncNodeManager nodeManager)
            {
                return m_nodeManager.TryGetTarget(out IAsyncNodeManager? candidate) &&
                    ReferenceEquals(candidate, nodeManager);
            }

            public bool Enabled { get; set; } = true;

            public int ActiveDispatches { get; set; }

            public TaskCompletionSource<bool>? DispatchesDrained { get; set; }

            public RetiredGenerationNotifications? Notifications { get; set; }

            private readonly WeakReference<IAsyncNodeManager> m_nodeManager;
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
        private readonly Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>>
            m_dynamicExternalReferences = [];

        private Dictionary<NodeId, IList<IReference>>? m_startupExternalReferences;

        private readonly Lock m_retiredGenerationNotificationsLock = new();
        private readonly List<RetiredGenerationNotifications>
            m_retiredGenerationNotifications = [];
        private readonly List<NotificationDispatchState>
            m_notificationDispatchStates = [];
        private volatile Action? m_retiredGenerationDrainObserver;

        private bool m_disposed;
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
        /// Initialize the MonitoredItemIdFactory with a new start value the ids start incrementing from.
        /// </summary>
        /// <param name="firstId"></param>
        public void SetStartValue(uint firstId)
        {
            Utils.SetIdentifier(ref m_lastMonitoredItemId, firstId);
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
        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 0, Level = LogLevel.Information,
            Message = "MasterNodeManager.Startup - NodeManagers={Count}")]
        public static partial void MasterNodeManagerStartupNodeManagersCount(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 1, Level = LogLevel.Error,
            Message = "Unexpected error creating address space for NodeManager ={NodeManager}.")]
        public static partial void UnexpectedErrorCreatingAddressSpaceForNodeManager(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 2, Level = LogLevel.Error,
            Message = "Unexpected error adding references for NodeManager ={NodeManager}.")]
        public static partial void UnexpectedErrorAddingReferencesForNodeManagerNodeManager(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 3, Level = LogLevel.Error,
            Message = "Unexpected error closing session for NodeManager ={NodeManager}.")]
        public static partial void UnexpectedErrorClosingSessionForNodeManagerNodeManager(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 4, Level = LogLevel.Error,
            Message = "Unexpected error notifying node manager of session activation for NodeManager={NodeManager}.")]
        public static partial void UnexpectedErrorNotifyingNodeManagerOfSession(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 5, Level = LogLevel.Information,
            Message = "MasterNodeManager.Shutdown - NodeManagers={Count}")]
        public static partial void MasterNodeManagerShutdownNodeManagersCount(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 6, Level = LogLevel.Warning,
            Message = "AddReferences: failed to mirror inverse edge {RefType} from {Source} to {Target} on " +
                "owning NodeManager: {Status}")]
        public static partial void AddReferencesFailedToMirrorInverseEdgeRefType(
            this ILogger logger,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target,
            StatusCode status);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 7, Level = LogLevel.Warning,
            Message = "AddReferences: failed to mirror inverse edge {RefType} from {Source} to {Target} on " +
                "owning NodeManager.")]
        public static partial void AddReferencesFailedToMirrorInverseEdgeRefType2(
            this ILogger logger,
            Exception ex,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 8, Level = LogLevel.Warning,
            Message = "DeleteReferences: failed to mirror inverse delete {RefType} from {Source} to {Target} " +
                "on owning NodeManager: {Status}")]
        public static partial void DeleteReferencesFailedToMirrorInverseDeleteRefType(
            this ILogger logger,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target,
            StatusCode status);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 9, Level = LogLevel.Warning,
            Message = "DeleteReferences: failed to mirror inverse delete {RefType} from {Source} to {Target} " +
                "on owning NodeManager.")]
        public static partial void DeleteReferencesFailedToMirrorInverseDeleteRefType2(
            this ILogger logger,
            Exception ex,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 10, Level = LogLevel.Trace,
            Message = "MasterNodeManager.RegisterNodes - Count={Count}")]
        public static partial void MasterNodeManagerRegisterNodesCountCount(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 11, Level = LogLevel.Trace,
            Message = "MasterNodeManager.UnregisterNodes - Count={Count}")]
        public static partial void MasterNodeManagerUnregisterNodesCountCount(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 12, Level = LogLevel.Error,
            Message = "Unexpected error translating browse path.")]
        public static partial void UnexpectedErrorTranslatingBrowsePath(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 13, Level = LogLevel.Trace,
            Message = "MasterNodeManager.Read - Count={Count}")]
        public static partial void MasterNodeManagerReadCountCount(this ILogger logger, int count);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 14, Level = LogLevel.Error,
            Message = "Error calling ConditionRefreshAsync on AsyncNodeManager.")]
        public static partial void ErrorCallingConditionRefreshAsyncOnAsyncNodeManager(
            this ILogger logger,
            Exception ex);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 15, Level = LogLevel.Error,
            Message = "NodeManager threw an exception subscribing to all events. NodeManager={NodeManager}")]
        public static partial void NodeManagerThrewAnExceptionSubscribingToAll(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 16, Level = LogLevel.Error,
            Message = "Failed to pre-hydrate queue for monitored item with id {MonitoredItemId}")]
        public static partial void FailedToPreHydrateQueueForMonitored(
            this ILogger logger,
            Exception ex,
            uint monitoredItemId);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 23, Level = LogLevel.Error,
            Message = "NodeManager threw an exception transferring monitored items. NodeManager={NodeManager}")]
        public static partial void MonitoredItemTransferFailedForNodeManager(
            this ILogger logger,
            Exception ex,
            string nodeManager);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 17, Level = LogLevel.Debug,
            Message = "Current user has no granted role.")]
        public static partial void CurrentUserHasNoGrantedRole(this ILogger logger);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 18, Level = LogLevel.Debug,
            Message = "Role permissions validation failed for node {NodeId}. Requested: {RequestedPermission}, " +
                "User has: {UserPermissions}")]
        public static partial void RolePermissionsValidationFailedForNodeNodeId(
            this ILogger logger,
            NodeId nodeId,
            PermissionType requestedPermission,
            PermissionType userPermissions);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 19, Level = LogLevel.Error,
            Message = "AddReferences: failed to roll back {RefType} from {Source} to {Target}: {Status}")]
        public static partial void AddReferencesRollbackFailed(
            this ILogger logger,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target,
            StatusCode status);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 20, Level = LogLevel.Error,
            Message = "AddReferences: failed to roll back {RefType} from {Source} to {Target}.")]
        public static partial void AddReferencesRollbackFailed2(
            this ILogger logger,
            Exception ex,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target);

        [LoggerMessage(EventId = ServerEventIds.MasterNodeManager + 21, Level = LogLevel.Error,
            Message = "DeleteReferences: failed to restore {RefType} from {Source} to {Target}: {Status}")]
        public static partial void DeleteReferencesRollbackFailed(
            this ILogger logger,
            NodeId refType,
            NodeId source,
            ExpandedNodeId target,
            StatusCode status);

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
