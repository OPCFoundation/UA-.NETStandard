// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns lifecycle mutation admission, optimistic publication sessions and durable-decision reentrancy.
    /// Operation ownership outlives mutation serialization, including reconciliation and retirement handoff.
    /// </summary>
    internal sealed class LifecycleWriter : IDisposable
    {
        /// <summary>
        /// Creates a writer whose synchronization remains usable while independent shutdown cleanup is pending.
        /// </summary>
        internal LifecycleWriter(Func<bool>? canDispose = null)
        {
            m_canDispose = canDispose ?? (() => true);
        }

        /// <summary>
        /// Gets whether shutdown has excluded ordinary operations.
        /// </summary>
        internal bool IsShuttingDown => m_shuttingDown;

        /// <summary>
        /// Gets the number of admitted lifecycle operations.
        /// </summary>
        internal int ActiveOperationCount
        {
            get
            {
                lock (m_state)
                {
                    return m_activeOperations;
                }
            }
        }

        /// <summary>
        /// Rejects operations that cannot be admitted by the running writer.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// A durable publication failed, or shutdown has excluded new operations.
        /// </exception>
        internal void EnsureRunning()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
            }
            if (m_publicationFailed)
            {
                throw new InvalidOperationException(
                    "The NodeManager lifecycle failed after a durable decision; restart the server to recover.");
            }
            if (m_shuttingDown)
            {
                throw new InvalidOperationException("The NodeManager lifecycle is shutting down.");
            }
        }

        /// <summary>
        /// Excludes further mutations after a durable decision could not publish its routing image.
        /// </summary>
        internal void MarkPublicationFailed()
        {
            lock (m_state)
            {
                m_publicationFailed = true;
            }
        }

        /// <summary>
        /// Restarts admission only after the previous shutdown and its owned operations have ended.
        /// </summary>
        internal void BeginStartup(Action reset)
        {
            lock (m_state)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
                }
                if (!m_shuttingDown)
                {
                    EnsureRunning();
                    return;
                }
                if (m_shutdownPrepared || m_activeOperations != 0 || m_activeShutdownMethods != 0)
                {
                    throw new InvalidOperationException(
                        "The previous NodeManager lifecycle shutdown did not complete.");
                }
                reset();
                m_publicationFailed = false;
                m_shuttingDown = false;
            }
        }

        /// <summary>
        /// Admits an operation independently of whether it currently owns mutation serialization.
        /// </summary>
        internal OperationLifetime EnterOperation()
        {
            lock (m_state)
            {
                EnsureRunning();
                m_activeOperations++;
                return new OperationLifetime(this);
            }
        }

        /// <summary>
        /// Acquires transferable mutation ownership. Application callbacks other than a durable decision
        /// must suspend or relinquish this ownership.
        /// </summary>
        internal async ValueTask<Mutation> BeginMutationAsync(CancellationToken cancellationToken)
        {
            await m_mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Mutation(this);
        }

        /// <summary>
        /// Begins independent invocation ownership without excluding other lifecycle operations.
        /// </summary>
        internal PublicationSession BeginPublication(PublicationRevision revision)
        {
            lock (m_state)
            {
                EnsureRunning();
                if (!ReferenceEquals(revision.Owner, this))
                {
                    throw new InvalidOperationException("The publication revision belongs to another writer.");
                }
                return new PublicationSession(this, revision);
            }
        }

        /// <summary>
        /// Excludes new operations and waits for all independently admitted operations, including invocations.
        /// Never holds mutation ownership while waiting for invocation disposal.
        /// </summary>
        internal ValueTask<Task> BeginShutdownAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_state)
            {
                if (m_synchronizationDisposed)
                {
                    throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
                }
                m_shuttingDown = true;
                m_activeShutdownMethods++;
                return new ValueTask<Task>(m_activeOperations == 0
                    ? Task.CompletedTask
                    : (m_operationsDrained ??= new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task);
            }
        }

        /// <summary>
        /// Ends a shutdown method and preserves an incomplete cleanup for a later retry.
        /// </summary>
        internal void EndShutdown(bool prepared, bool completing = false)
        {
            lock (m_state)
            {
                if (prepared || completing)
                {
                    m_shutdownPrepared = prepared;
                }
                m_activeShutdownMethods--;
                TryDisposeSynchronization();
            }
        }

        /// <summary>
        /// Serializes shutdown cleanup separately from mutation ownership.
        /// </summary>
        internal async ValueTask<ShutdownCleanup> BeginShutdownCleanupAsync(CancellationToken cancellationToken)
        {
            await m_shutdownCleanup.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new ShutdownCleanup(this);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_state)
            {
                m_disposed = true;
                m_shuttingDown = true;
                TryDisposeSynchronization();
            }
        }

        /// <summary>
        /// Rejects lifecycle entry from this writer's active decision.
        /// </summary>
        internal void ThrowIfDecisionCallbackActive(NodeManagerDecisionContext context)
        {
            if (context.WriterId == m_decisionIdentity && context.IsActive)
            {
                throw new InvalidOperationException(
                    "NodeManager lifecycle operations cannot be started from the durable decision callback.");
            }
        }

        /// <summary>
        /// Ends operation admission after all of the operation's writer-free work has finished.
        /// </summary>
        private void ExitOperation()
        {
            TaskCompletionSource<bool>? drained = null;
            lock (m_state)
            {
                if (--m_activeOperations == 0)
                {
                    drained = m_operationsDrained;
                    m_operationsDrained = null;
                }
                TryDisposeSynchronization();
            }
            drained?.TrySetResult(true);
        }

        /// <summary>
        /// Disposes mutation synchronization only when no admitted operation can still use it.
        /// </summary>
        private void TryDisposeSynchronization()
        {
            if (m_disposed &&
                !m_synchronizationDisposed &&
                m_activeOperations == 0 &&
                m_activeShutdownMethods == 0 &&
                !m_shutdownPrepared &&
                m_canDispose())
            {
                m_synchronizationDisposed = true;
                m_mutation.Dispose();
                m_shutdownCleanup.Dispose();
            }
        }

        /// <summary>
        /// Captures every serving revision used to admit a publication invocation.
        /// </summary>
        internal PublicationRevision CaptureRevision(
            IServerInternal server,
            IDynamicNodeManagerHost host)
        {
            EnsureRunning();
            if (server.Factory is not EncodeableFactory factory)
            {
                throw new NotSupportedException("The server cannot capture publication revisions.");
            }
            return CaptureRevisionCore(server, host, factory);
        }

        /// <summary>
        /// Captures serving metadata for an already owned publication, including during shutdown drain.
        /// </summary>
        private PublicationRevision CaptureRevisionCore(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            EncodeableFactory factory)
        {
            _ = server.TypeTree.CaptureSnapshot(out TypeTable types, out long typeRevision);
            _ = factory.CaptureSnapshot(out EncodeableFactory originalFactory, out long factoryRevision);
            return new PublicationRevision(
                this, server, host, host.RoutingRevision, types, typeRevision,
                originalFactory, factoryRevision);
        }

        /// <summary>
        /// Non-owning revision evidence checked before preparation and the durable publication decision.
        /// </summary>
        internal sealed class PublicationRevision
        {
            /// <summary>
            /// Records exact serving identities without extending server or superseded image lifetimes.
            /// </summary>
            internal PublicationRevision(
                LifecycleWriter owner,
                IServerInternal server,
                IDynamicNodeManagerHost host,
                NodeManagerRoutingTable.RoutingSnapshot routingRevision,
                TypeTable types,
                long typeRevision,
                EncodeableFactory factory,
                long factoryRevision)
            {
                Owner = owner;
                m_server = new WeakReference<IServerInternal>(server);
                m_host = new WeakReference<IDynamicNodeManagerHost>(host);
                m_routingRevision = new WeakReference<NodeManagerRoutingTable.RoutingSnapshot>(routingRevision);
                m_types = new WeakReference<TypeTable>(types);
                m_factory = new WeakReference<EncodeableFactory>(factory);
                m_typeRevision = typeRevision;
                m_factoryRevision = factoryRevision;
            }

            /// <summary>
            /// Gets the synchronization owner, which holds no server or candidate ownership.
            /// </summary>
            internal LifecycleWriter Owner { get; }

            /// <summary>
            /// Gets the server only while its original identity remains available.
            /// </summary>
            /// <exception cref="ObjectDisposedException">The captured server is no longer available.</exception>
            internal IServerInternal Server => m_server.TryGetTarget(out IServerInternal? server)
                ? server
                : throw new ObjectDisposedException(nameof(INodeManagerLifecycle));

            /// <summary>
            /// Gets the exact original host without recapturing serving evidence.
            /// </summary>
            /// <exception cref="ObjectDisposedException">The captured host is no longer available.</exception>
            internal IDynamicNodeManagerHost OwnerHost => m_host.TryGetTarget(out IDynamicNodeManagerHost? host)
                ? host
                : throw new ObjectDisposedException(nameof(INodeManagerLifecycle));

            /// <summary>
            /// Checks server incarnation without recapturing any routing or metadata evidence.
            /// </summary>
            internal bool MatchesServer(IServerInternal server)
            {
                return m_server.TryGetTarget(out IServerInternal? captured) &&
                    ReferenceEquals(captured, server) &&
                    m_host.TryGetTarget(out IDynamicNodeManagerHost? host) &&
                    ReferenceEquals(server.NodeManager, host);
            }

            /// <summary>
            /// Checks routing, type and encodeable revisions together.
            /// </summary>
            internal bool IsCurrent()
            {
                if (Owner.m_publicationFailed ||
                    !m_server.TryGetTarget(out IServerInternal? server) ||
                    !m_host.TryGetTarget(out IDynamicNodeManagerHost? host) ||
                    !m_routingRevision.TryGetTarget(out NodeManagerRoutingTable.RoutingSnapshot? routing) ||
                    !m_types.TryGetTarget(out TypeTable? types) ||
                    !m_factory.TryGetTarget(out EncodeableFactory? expectedFactory) ||
                    server.Factory is not EncodeableFactory currentFactory)
                {
                    return false;
                }
                _ = currentFactory.CaptureSnapshot(
                    out EncodeableFactory factory, out long factoryRevision);
                return ReferenceEquals(host.RoutingRevision, routing) &&
                    server.TypeTree.IsCurrentSnapshot(types, m_typeRevision) &&
                    ReferenceEquals(factory, expectedFactory) &&
                    factoryRevision == m_factoryRevision;
            }

            /// <summary>The server identity, without lifetime ownership.</summary>
            private readonly WeakReference<IServerInternal> m_server;

            /// <summary>The host identity, without lifetime ownership.</summary>
            private readonly WeakReference<IDynamicNodeManagerHost> m_host;

            /// <summary>The original routing image, without retaining its generations.</summary>
            private readonly WeakReference<NodeManagerRoutingTable.RoutingSnapshot> m_routingRevision;

            /// <summary>The original type image, without retaining superseded types.</summary>
            private readonly WeakReference<TypeTable> m_types;

            /// <summary>The original factory image, without retaining superseded types.</summary>
            private readonly WeakReference<EncodeableFactory> m_factory;

            /// <summary>The original type-table revision.</summary>
            private readonly long m_typeRevision;

            /// <summary>The original factory revision.</summary>
            private readonly long m_factoryRevision;
        }

        /// <summary>
        /// Transferable mutation ownership that can end without releasing operation admission.
        /// </summary>
        internal sealed class Mutation(LifecycleWriter owner) : IDisposable
        {
            /// <summary>
            /// Opens a decision window without binding it to the caller's execution context.
            /// </summary>
            /// <exception cref="InvalidOperationException">
            /// The mutation is not held or already has a decision window.
            /// </exception>
            internal DecisionWindow EnterDecisionWindow()
            {
                if (!m_held)
                {
                    throw new InvalidOperationException("The durable decision requires mutation ownership.");
                }
                var scope = new NodeManagerDecisionContext(owner.m_decisionIdentity);
                if (Interlocked.CompareExchange(ref m_activeDecision, scope, null) is not null)
                {
                    throw new InvalidOperationException("The mutation already has a durable decision window.");
                }
                return new DecisionWindow(this, owner, scope);
            }

            /// <summary>
            /// Invokes the sole callback allowed under mutation ownership.
            /// </summary>
            internal ValueTask DecideAsync(
                Func<NodeManagerDecisionContext, CancellationToken, ValueTask> callback,
                CancellationToken cancellationToken)
            {
                return DecideAsync(callback, null, cancellationToken);
            }

            /// <summary>
            /// Executes the decision using the host's reservation, or owns a local reservation if there is no host.
            /// </summary>
            internal async ValueTask DecideAsync(
                Func<NodeManagerDecisionContext, CancellationToken, ValueTask> callback,
                DecisionWindow? reservation,
                CancellationToken cancellationToken)
            {
                if (!m_held)
                {
                    throw new InvalidOperationException("The durable decision requires mutation ownership.");
                }
                using DecisionWindow? local = reservation is null ? EnterDecisionWindow() : null;
                DecisionWindow window = reservation ?? local!;
                NodeManagerDecisionContext scope = window.GetScope(this);
                scope.Activate();
                try
                {
                    await callback(scope, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    scope.Deactivate();
                }
            }

            /// <summary>
            /// Checks whether this mutation is still held by the specified writer.
            /// </summary>
            internal bool IsHeldBy(LifecycleWriter writer)
            {
                return m_held && ReferenceEquals(owner, writer);
            }

            /// <summary>
            /// Relinquishes mutation ownership without ending operation admission.
            /// </summary>
            public void Dispose()
            {
                Release();
            }

            /// <summary>
            /// Releases this owner's semaphore admission exactly once.
            /// </summary>
            private void Release()
            {
                if (m_held)
                {
                    m_held = false;
                    owner.m_mutation.Release();
                }
            }

            /// <summary>The exact reservation for this mutation, cleared on release.</summary>
            private NodeManagerDecisionContext? m_activeDecision;

            /// <summary>Whether this owner currently holds mutation admission.</summary>
            private bool m_held = true;

            /// <summary>
            /// Revokes the marker shared by the host and writer before relinquishing the window.
            /// </summary>
            internal sealed class DecisionWindow(
                Mutation mutation,
                LifecycleWriter writer,
                NodeManagerDecisionContext scope) : IDisposable
            {
                /// <summary>
                /// Determines whether the explicit context identifies this open reservation.
                /// </summary>
                internal bool Includes(NodeManagerDecisionContext context)
                {
                    return ReferenceEquals(scope, context) && scope.IsOpen;
                }

                /// <summary>
                /// Identifies re-entry from this reservation's current flow.
                /// </summary>
                internal bool IsInCurrentFlow =>
                    scope.IsOpen && ReferenceEquals(writer.m_namespaceGetter.Value, scope);

                /// <summary>
                /// Adapts a callback that cannot receive a reservation. The bound marker carries no
                /// reservation owner and remains revocable for any flow captured by the callback.
                /// </summary>
                /// <exception cref="InvalidOperationException">The reservation has closed.</exception>
                internal CallbackFlow EnterCallbackFlow()
                {
                    if (!scope.IsOpen)
                    {
                        throw new InvalidOperationException("The publication reservation has already closed.");
                    }
                    var flow = new CallbackFlow(writer, writer.m_namespaceGetter.Value);
                    writer.m_namespaceGetter.Value = scope;
                    return flow;
                }

                /// <summary>
                /// Requires the live reservation owned by this exact mutation.
                /// </summary>
                /// <exception cref="InvalidOperationException">
                /// The reservation has closed or belongs to another mutation.
                /// </exception>
                internal NodeManagerDecisionContext GetScope(Mutation expected)
                {
                    if (!scope.IsOpen || !ReferenceEquals(mutation, expected))
                    {
                        throw new InvalidOperationException(
                            "The decision reservation is not active for this mutation.");
                    }
                    return scope;
                }

                /// <inheritdoc/>
                public void Dispose()
                {
                    if (!scope.Close())
                    {
                        return;
                    }
                    Interlocked.CompareExchange(ref mutation.m_activeDecision, null, scope);
                }

                /// <summary>
                /// Restores the caller's context after invoking a callback.
                /// </summary>
                internal readonly struct CallbackFlow(
                    LifecycleWriter writer, NodeManagerDecisionContext? previous) : IDisposable
                {
                    /// <inheritdoc/>
                    public void Dispose()
                    {
                        writer.m_namespaceGetter.Value = previous;
                    }
                }
            }
        }

        /// <summary>
        /// Owns an invocation's optimistic revision, sequential preparation and prepared transaction disposal.
        /// </summary>
        internal sealed class PublicationSession : IAsyncDisposable
        {
            /// <summary>
            /// Owns writer admission until terminal disposal, retaining only private synchronization afterward.
            /// </summary>
            internal PublicationSession(LifecycleWriter owner, PublicationRevision revision)
            {
                m_state = owner.m_state;
                m_revision = revision;
                m_operation = owner.EnterOperation();
            }

            /// <summary>
            /// Gets whether this invocation's expected serving revisions remain current.
            /// </summary>
            internal bool IsCurrent
            {
                get
                {
                    lock (m_state)
                    {
                        return !m_closing && Revision.IsCurrent();
                    }
                }
            }

            /// <summary>
            /// Rejects stale captured evidence under mutation ownership before preparation or a durable decision.
            /// </summary>
            internal void ValidateRevision(IServerInternal? server = null)
            {
                lock (m_state)
                {
                    PublicationRevision revision = Revision;
                    if (!revision.IsCurrent() || (server is not null && !revision.MatchesServer(server)))
                    {
                        throw new InvalidOperationException(
                            "The publication invocation conflicts with a newer address-space revision.");
                    }
                }
            }

            /// <summary>
            /// Checks the lifecycle's current server incarnation as well as captured serving revisions.
            /// </summary>
            internal bool IsCurrentFor(INodeManagerLifecycleHost host)
            {
                if (Volatile.Read(ref m_closing))
                {
                    return false;
                }
                IServerInternal server = host.CurrentInstance;
                lock (m_state)
                {
                    return !m_closing && Revision.MatchesServer(server) && Revision.IsCurrent();
                }
            }

            /// <summary>
            /// Observes only this invocation's successful publication, immediately after its swap under the writer.
            /// </summary>
            internal void AdvanceRevision()
            {
                lock (m_state)
                {
                    PublicationRevision revision = Revision;
                    m_revision = revision.Owner.CaptureRevisionCore(
                        revision.Server, revision.OwnerHost, (EncodeableFactory)revision.Server.Factory);
                }
            }

            /// <summary>
            /// Gets completion of invocation disposal, including its transactions.
            /// </summary>
            internal Task Finished => m_finished.Task;

            /// <summary>
            /// Gets active revision ownership, including while admitted work drains during disposal.
            /// </summary>
            /// <exception cref="ObjectDisposedException">The publication revision has been released.</exception>
            private PublicationRevision Revision =>
                m_revision ?? throw new ObjectDisposedException(nameof(ILifecycleWriter));

            /// <summary>
            /// Prepares one transaction while preventing overlapping preparation or premature disposal.
            /// </summary>
            /// <typeparam name="T">The prepared transaction type.</typeparam>
            /// <exception cref="ObjectDisposedException">
            /// The publication invocation is closing or its revision has been released.
            /// </exception>
            internal async ValueTask<T> PrepareAsync<T>(Func<ValueTask<T>> prepare)
                where T : IAsyncDisposable
            {
                TaskCompletionSource<bool> preparation;
                lock (m_state)
                {
                    LifecycleWriter owner = Revision.Owner;
                    owner.EnsureRunning();
                    if (m_closing)
                    {
                        throw new ObjectDisposedException(nameof(ILifecycleWriter));
                    }
                    ValidateRevision();
                    if (m_prepared is not null)
                    {
                        throw new InvalidOperationException("Prepare invocation units sequentially.");
                    }
                    preparation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_prepared = preparation;
                }
                try
                {
                    T batch = await prepare().ConfigureAwait(false);
                    lock (m_state)
                    {
                        m_batches.Add(batch);
                    }
                    return batch;
                }
                finally
                {
                    lock (m_state)
                    {
                        m_prepared = null;
                        preparation.TrySetResult(true);
                    }
                }
            }

            /// <summary>
            /// Releases an exact consumed unit after publication or transfer to lifecycle-owned cleanup.
            /// </summary>
            /// <param name="batch">The prepared unit whose cleanup finished.</param>
            /// <param name="failure">An earlier disposal failure that the invocation must still report.</param>
            internal void ReleaseCompletedBatch(IAsyncDisposable batch, Exception? failure = null)
            {
                lock (m_state)
                {
                    if (m_closing)
                    {
                        return;
                    }
                    bool found = false;
                    for (int index = m_batches.Count - 1; index >= 0; index--)
                    {
                        if (ReferenceEquals(m_batches[index], batch))
                        {
                            m_batches.RemoveAt(index);
                            found = true;
                        }
                    }
                    if (found && failure is not null)
                    {
                        (m_completedDisposalFailures ??= []).Add(failure);
                    }
                }
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                Task? prepared;
                bool ownsDisposal;
                lock (m_state)
                {
                    ownsDisposal = !m_closing;
                    m_closing = true;
                    prepared = m_prepared?.Task;
                }
                if (!ownsDisposal)
                {
                    await Finished.ConfigureAwait(false);
                    return;
                }
                Exception? cleanupFailure = null;
                try
                {
                    if (prepared is not null)
                    {
                        await prepared.ConfigureAwait(false);
                    }
                    // Closing excludes further releases, so disposal now owns the retained failure list.
                    List<Exception> failures = m_completedDisposalFailures ?? [];
                    foreach (IAsyncDisposable batch in m_batches)
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
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    cleanupFailure = failure;
                }
                catch (OutOfMemoryException failure)
                {
                    cleanupFailure = failure;
                    throw;
                }
                finally
                {
                    lock (m_state)
                    {
                        m_batches.Clear();
                        m_completedDisposalFailures?.Clear();
                        m_revision = null;
                    }
                    m_operation.Dispose();
                    if (cleanupFailure is null)
                    {
                        m_finished.TrySetResult(true);
                    }
                    else
                    {
                        m_finished.TrySetException(cleanupFailure);
                    }
                }
                await Finished.ConfigureAwait(false);
            }

            /// <summary>Transactions retained until their cleanup or invocation disposal.</summary>
            private readonly List<IAsyncDisposable> m_batches = [];

            /// <summary>Earlier disposal failures retained after their units finish physical cleanup.</summary>
            private List<Exception>? m_completedDisposalFailures;

            /// <summary>Expected serving metadata, advanced only by this invocation's own publication.</summary>
            private PublicationRevision? m_revision;

            /// <summary>Outcome shared by callers joining the active cleanup attempt.</summary>
            private readonly TaskCompletionSource<bool> m_finished =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Completion of the currently admitted preparation.</summary>
            private TaskCompletionSource<bool>? m_prepared;

            /// <summary>The operation admitted and owned by this invocation until its cleanup finishes.</summary>
            private readonly OperationLifetime m_operation;

            /// <summary>Private synchronization that remains usable after writer ownership is released.</summary>
            private readonly Lock m_state;

            /// <summary>Whether disposal has excluded further preparation.</summary>
            private bool m_closing;
        }

        /// <summary>
        /// Operation admission that survives writer suspension and prevents shutdown teardown.
        /// </summary>
        internal sealed class OperationLifetime : IDisposable
        {
            /// <summary>
            /// Creates operation ownership without transferring ownership of the writer.
            /// </summary>
            internal OperationLifetime(LifecycleWriter owner)
            {
                m_owner = owner;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                LifecycleWriter? writer = Interlocked.Exchange(ref m_owner, null);
                if (writer is not null)
                {
                    writer.ExitOperation();
                }
            }

            /// <summary>The writer until this operation ends.</summary>
            private LifecycleWriter? m_owner;
        }

        /// <summary>
        /// Releases independent shutdown-cleanup admission.
        /// </summary>
        internal sealed class ShutdownCleanup(LifecycleWriter owner) : IDisposable
        {
            /// <summary>
            /// Admits a removal owned by this exact shutdown invocation, without reopening public admission.
            /// </summary>
            internal OperationLifetime EnterOperation()
            {
                lock (owner.m_state)
                {
                    Validate(owner);
                    owner.m_activeOperations++;
                    return new OperationLifetime(owner);
                }
            }

            /// <summary>
            /// Rejects a disposed capability or one belonging to a different writer.
            /// </summary>
            internal void Validate(LifecycleWriter writer)
            {
                if (!ReferenceEquals(owner, writer) ||
                    Volatile.Read(ref m_disposed) != 0 ||
                    !owner.IsShuttingDown)
                {
                    throw new InvalidOperationException("The shutdown publication no longer owns writer admission.");
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                {
                    owner.m_shutdownCleanup.Release();
                }
            }

            /// <summary>Whether this invocation has released shutdown ownership.</summary>
            private int m_disposed;
        }

        /// <summary>Checks whether independent cleanup still requires shutdown synchronization.</summary>
        private readonly Func<bool> m_canDispose;

        /// <summary>Mutation serialization, never used by reclamation.</summary>
        private readonly SemaphoreSlim m_mutation = new(1, 1);

        /// <summary>Shutdown cleanup serialization, independent of the mutation writer.</summary>
        private readonly SemaphoreSlim m_shutdownCleanup = new(1, 1);

        /// <summary>Protects operation and invocation admission.</summary>
        private readonly Lock m_state = new();

        /// <summary>The adapter for context-free namespace getters.</summary>
        private readonly AsyncLocal<NodeManagerDecisionContext?> m_namespaceGetter = new();

        /// <summary>Identifies this writer without retaining it in a callback context.</summary>
        private readonly Guid m_decisionIdentity = Guid.NewGuid();

        /// <summary>Completion of outstanding operations after shutdown admission.</summary>
        private TaskCompletionSource<bool>? m_operationsDrained;

        /// <summary>Operations owning shutdown exclusion.</summary>
        private int m_activeOperations;

        /// <summary>Shutdown methods that may still acquire mutation ownership.</summary>
        private int m_activeShutdownMethods;

        /// <summary>Whether shutdown cleanup remains outstanding.</summary>
        private bool m_shutdownPrepared;

        /// <summary>Whether mutation synchronization has been disposed.</summary>
        private bool m_synchronizationDisposed;

        /// <summary>Whether disposal has excluded new admission.</summary>
        private volatile bool m_disposed;

        /// <summary>Whether shutdown has excluded new admission.</summary>
        private volatile bool m_shuttingDown;

        /// <summary>Whether a durable decision failed to install its routing image.</summary>
        private volatile bool m_publicationFailed;
    }
}
