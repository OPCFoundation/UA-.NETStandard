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
                    EnterRequestLifecycleWaiter(server);
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
                    candidates, removed,
                    retired.Where(generation => generation.DetachActiveMonitoredItems)
                        .Select(generation => generation.NodeManager).ToArrayOf(),
                    batch.RoutingRevision, batch.TypeTree, batch.OriginalTypes,
                    batch.TypeRevision, batch.Factory, batch.OriginalFactory, batch.FactoryRevision,
                    batch.ReadImages, decideAsync,
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
                    failures.Add,
                    cancellationToken).ConfigureAwait(false);
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
    }
}
