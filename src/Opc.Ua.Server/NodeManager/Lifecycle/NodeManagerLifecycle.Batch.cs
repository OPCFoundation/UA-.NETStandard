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

namespace Opc.Ua.Server
{
    public sealed partial class NodeManagerLifecycle
    {
        /// <inheritdoc/>
        public async ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default)
        {
            if (changes.IsNull || changes.Count == 0)
            {
                throw new ArgumentException("A publication unit must contain at least one change.", nameof(changes));
            }
            changes = [.. changes];
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
                    allowRequestCallback &= IsRequestCallbackSafe(factory);
                }
            }

            var entries = new List<BatchEntry>();
            OperationLifetime? operation = null;
            try
            {
                operation = EnterLifecycleOperation();
                (IServerInternal server, IDynamicNodeManagerHost host) =
                    GetRunningServer(allowRequestCallback);
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
                    EnterRequestLifecycleWaiter(server);
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
                            ReadinessPending = entry.Manager is INodeManagerReadinessParticipant
                        };
                    }
                    // Staging can register hidden namespace routes; freeze its completed routing image.
                    var prepared = new PreparedBatch(
                        this, server, host, entries, operation, namespaceCount, allowRequestCallback,
                        batchHost.RoutingRevision, preparedTypes, originalTypes, typeRevision,
                        preparedFactory, originalFactory, factoryRevision, resolver);
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

        private async ValueTask<NodeManagerBatchResult> CommitBatchAsync(
            PreparedBatch batch,
            Func<CancellationToken, ValueTask> decideAsync,
            CancellationToken cancellationToken,
            Action? publishCommittedState = null)
        {
            using IDisposable routing = ((IDynamicNodeManagerBatchHost)batch.Host).UseLiveRouting();
            using RequestManagerLifecycleExtension.RequestLifecycleWaiterScope? waiter =
                EnterRequestLifecycleWaiter(batch.Server);
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
                    candidates, removed, batch.RoutingRevision, batch.TypeTree, batch.OriginalTypes,
                    batch.TypeRevision, batch.Factory, batch.OriginalFactory, batch.FactoryRevision, decideAsync,
                    () =>
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
                    async () =>
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
                                    batch.Server, entry.Manager, new ServerBindings(), CancellationToken.None)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception failure) when (failure is not OutOfMemoryException)
                            {
                                failures.Add(failure);
                            }
                        }
                    },
                    failures.Add,
                    cancellationToken).ConfigureAwait(false);

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
                    await NotifyCommittedChangeAsync(
                        batch.Server, "batch", batch.NamespaceCount, CancellationToken.None).ConfigureAwait(false);
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

        private sealed class BatchEntry
        {
            public BatchEntry(NodeManagerBatchChange change, RegistrationState? current)
            {
                Change = change;
                Current = current;
            }

            public NodeManagerBatchChange Change { get; }
            public RegistrationState? Current { get; }
            public IAsyncNodeManager? Manager { get; set; }
            public PreparedNodeManager? Prepared { get; set; }
            public RegistrationState? Next { get; set; }
            public List<LocalReference> DroppedReferences { get; set; } = [];
        }

        private sealed class PreparedBatch : IPreparedNodeManagerBatch
        {
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
                IDataTypeDefinitionResolver? resolver)
            {
                m_owner = owner;
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
                Registrations = entries.Where(entry => entry.Next is not null)
                    .Select(entry => entry.Next!.Registration).ToArrayOf();
            }

            public ArrayOf<NodeManagerRegistration> Registrations { get; }
            public bool IsCommitted { get; set; }
            public IServerInternal Server { get; }
            public IDynamicNodeManagerHost Host { get; }
            public List<BatchEntry> Entries { get; }
            public int NamespaceCount { get; }
            public bool AllowRequestCallback { get; }
            public NodeManagerRoutingTable.RoutingSnapshot RoutingRevision { get; }
            public TypeTable TypeTree { get; }
            public TypeTable OriginalTypes { get; }
            public long TypeRevision { get; }
            public EncodeableFactory Factory { get; }
            public EncodeableFactory OriginalFactory { get; }
            public long FactoryRevision { get; }
            public IDataTypeDefinitionResolver? Resolver { get; }

            public ValueTask<NodeManagerBatchResult> CommitAsync(
                Func<CancellationToken, ValueTask> decideAsync,
                CancellationToken cancellationToken = default)
            {
                return CommitCoreAsync(decideAsync, null, cancellationToken);
            }

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

            public async ValueTask DisposeAsync()
            {
                int state = Interlocked.CompareExchange(ref m_state, 2, 0);
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
                if (Interlocked.CompareExchange(ref m_state, 1, 0) != 0)
                {
                    throw new InvalidOperationException("The prepared batch has already been consumed.");
                }
                try
                {
                    return await m_owner.CommitBatchAsync(
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
            private readonly TaskCompletionSource<bool> m_finished =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private OperationLifetime? m_operation;
            private int m_state;
        }
    }
}
