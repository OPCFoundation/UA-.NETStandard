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
                m_unpublishedRoutingPositions.Clear();

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
                    StartupNodeManagerState? startupState =
                        FindStartupApplicationNodeManager(nodeManager);
                    Dictionary<NodeId, List<ExternalReferenceSnapshot>>? referencesBefore =
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
                prepared = true;
                return new PreparedNodeManager(nodeManager, externalReferences);
            }
            catch (Exception ex) when (
                ex is not OutOfMemoryException and
                    not NodeManagerAlreadyRegisteredException)
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
                    NodeManagerRoutingTable.NodeManagerRoutingPosition? routingPosition = null;
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
                        routingPosition =
                            m_nodeManagers.RemoveAndCapturePosition(nodeManager);
                        routeRemoved = true;
                        m_dynamicExternalReferences.Remove(nodeManager);
                        m_unpublishedRoutingPositions[nodeManager] =
                            routingPosition!;
                    }
                    catch
                    {
                        if (routeRemoved)
                        {
                            m_nodeManagers.Restore(
                                nodeManager,
                                routingPosition!,
                                visible: false);
                            m_unpublishedRoutingPositions.Remove(nodeManager);
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
                        AllowLifecycleFromRequestCallback =
                            state.NodeManager is IRequestCallbackSafeNodeManager
                            {
                                AllowLifecycleFromRequestCallback: true
                            },
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

            RemoveRetiredGenerationNotifications(nodeManager);
            m_unpublishedRoutingPositions.Remove(nodeManager);
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
            bool restoringPosition = m_unpublishedRoutingPositions.TryGetValue(
                prepared.NodeManager,
                out NodeManagerRoutingTable.NodeManagerRoutingPosition? routingPosition);
            try
            {
                if (restoringPosition)
                {
                    m_nodeManagers.Restore(
                        prepared.NodeManager,
                        routingPosition!);
                }
                else
                {
                    m_nodeManagers.Add(
                        prepared.NodeManager,
                        ResolveNamespaceIndexes(prepared.NodeManager));
                }
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
                m_unpublishedRoutingPositions.Remove(prepared.NodeManager);
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
                        m_unpublishedRoutingPositions.Remove(
                            prepared.NodeManager);
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
            int[] replacementNamespaceIndexes =
                ResolveNamespaceIndexes(prepared.NodeManager);
            bool currentWasVisible = m_nodeManagers.IsVisible(current);
            bool currentReferenceMutationStarted = false;
            bool replacementReferenceMutationStarted = false;
            bool routeReplaced = false;
            bool retiredNotificationsRetained = false;
            NodeManagerRoutingTable.NodeManagerRoutingPosition?
                currentRoutingPosition = null;
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
                currentRoutingPosition = m_nodeManagers.Replace(
                    current,
                    prepared.NodeManager,
                    replacementNamespaceIndexes);
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
                        m_nodeManagers.RestoreReplacement(
                            prepared.NodeManager,
                            current,
                            currentRoutingPosition!,
                            visible: false);
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

        private static Dictionary<NodeId, List<ExternalReferenceSnapshot>>
            SnapshotExternalReferences(
                IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            var snapshot =
                new Dictionary<NodeId, List<ExternalReferenceSnapshot>>();
            foreach (KeyValuePair<NodeId, IList<IReference>> entry in externalReferences)
            {
                var references =
                    new List<ExternalReferenceSnapshot>(entry.Value.Count);
                foreach (IReference reference in entry.Value)
                {
                    references.Add(new ExternalReferenceSnapshot(reference));
                }
                snapshot.Add(entry.Key, references);
            }
            return snapshot;
        }

        private static Dictionary<NodeId, IList<IReference>>
            CaptureAddedExternalReferences(
                Dictionary<NodeId, List<ExternalReferenceSnapshot>> before,
                IDictionary<NodeId, IList<IReference>> after)
        {
            var additions = new Dictionary<NodeId, IList<IReference>>();
            foreach (KeyValuePair<NodeId, IList<IReference>> entry in after)
            {
                before.TryGetValue(
                    entry.Key,
                    out List<ExternalReferenceSnapshot>? previous);
                var matched = new bool[previous?.Count ?? 0];

                foreach (IReference reference in entry.Value)
                {
                    int match = -1;
                    if (previous is not null)
                    {
                        for (int ii = 0; ii < previous.Count; ii++)
                        {
                            if (!matched[ii] && previous[ii].Matches(reference))
                            {
                                match = ii;
                                break;
                            }
                        }
                    }
                    if (match >= 0)
                    {
                        matched[match] = true;
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
                        if (ExternalReferenceSnapshot.Matches(
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

        private sealed class StartupNodeManagerState
        {
            public StartupNodeManagerState(IAsyncNodeManager nodeManager)
            {
                NodeManager = nodeManager;
            }

            public IAsyncNodeManager NodeManager { get; }

            public Dictionary<NodeId, IList<IReference>>? ExternalReferences { get; set; }
        }

        private sealed class ExternalReferenceSnapshot
        {
            public ExternalReferenceSnapshot(IReference reference)
            {
                ReferenceTypeId = reference.ReferenceTypeId;
                IsInverse = reference.IsInverse;
                TargetId = reference.TargetId;
            }

            public NodeId ReferenceTypeId { get; }

            public bool IsInverse { get; }

            public ExpandedNodeId TargetId { get; }

            public bool Matches(IReference reference)
            {
                return Matches(
                    ReferenceTypeId,
                    IsInverse,
                    TargetId,
                    reference);
            }

            public static bool Matches(
                IReference left,
                IReference right)
            {
                return Matches(
                    left.ReferenceTypeId,
                    left.IsInverse,
                    left.TargetId,
                    right);
            }

            private static bool Matches(
                NodeId referenceTypeId,
                bool isInverse,
                ExpandedNodeId targetId,
                IReference reference)
            {
                return referenceTypeId == reference.ReferenceTypeId &&
                    isInverse == reference.IsInverse &&
                    targetId == reference.TargetId;
            }
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
        private readonly Dictionary<IAsyncNodeManager, Dictionary<NodeId, IList<IReference>>>
            m_dynamicExternalReferences = [];
        private readonly Dictionary<
            IAsyncNodeManager,
            NodeManagerRoutingTable.NodeManagerRoutingPosition>
            m_unpublishedRoutingPositions = [];

        private Dictionary<NodeId, IList<IReference>>? m_startupExternalReferences;

        private readonly Lock m_retiredGenerationNotificationsLock = new();
        private readonly List<RetiredGenerationNotifications>
            m_retiredGenerationNotifications = [];
        private readonly List<NotificationDispatchState>
            m_notificationDispatchStates = [];
        private volatile Action? m_retiredGenerationDrainObserver;

        private bool m_startupApplicationNodeManagersTransferred;
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
