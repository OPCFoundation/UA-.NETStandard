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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Owns lifecycle mutation admission, publication reservations and durable-decision reentrancy.
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
        /// Rejects operations that cannot be admitted by the running writer.
        /// </summary>
        internal void EnsureRunning()
        {
            ThrowIfDecisionCallbackActive();
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
            }
            if (m_shuttingDown)
            {
                throw new InvalidOperationException("The NodeManager lifecycle is shutting down.");
            }
        }

        /// <summary>
        /// Restarts admission only after the previous shutdown and its owned operations have ended.
        /// </summary>
        internal void BeginStartup(Action reset)
        {
            ThrowIfDecisionCallbackActive();
            lock (m_state)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
                }
                if (!m_shuttingDown)
                {
                    return;
                }
                if (m_shutdownPrepared || m_activeOperations != 0 || m_activeShutdownMethods != 0)
                {
                    throw new InvalidOperationException(
                        "The previous NodeManager lifecycle shutdown did not complete.");
                }
                reset();
                m_shuttingDown = false;
            }
        }

        /// <summary>
        /// Admits an operation independently of whether it currently owns mutation serialization.
        /// </summary>
        internal OperationLifetime EnterOperation(PublicationReservation? reservation = null)
        {
            ThrowIfDecisionCallbackActive();
            lock (m_state)
            {
                EnsureRunning();
                if (m_publicationFinished is not null && m_publicationId != reservation?.Id)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadServerTooBusy, "An invocation owns lifecycle publication admission.");
                }
                if (reservation is not null && m_publicationId != reservation.Id)
                {
                    throw new ObjectDisposedException(nameof(INodeManagerPublication));
                }
                m_activeOperations++;
                var operation = new OperationLifetime(this, m_currentOperation.Value);
                m_currentOperation.Value = operation;
                return operation;
            }
        }

        /// <summary>
        /// Acquires transferable mutation ownership. Application callbacks other than a durable decision
        /// must suspend or relinquish this ownership.
        /// </summary>
        internal async ValueTask<Mutation> BeginMutationAsync(
            CancellationToken cancellationToken,
            Action? waiting = null)
        {
            ThrowIfDecisionCallbackActive();
            Task pending = m_mutation.WaitAsync(cancellationToken);
            waiting?.Invoke();
            await pending.ConfigureAwait(false);
            return new Mutation(this);
        }

        /// <summary>
        /// Reserves an invocation before any candidate factory can run, capturing revision validity once.
        /// </summary>
        internal PublicationReservation ReservePublication(PublicationRevision revision)
        {
            ThrowIfDecisionCallbackActive();
            lock (m_state)
            {
                EnsureRunning();
                int ancestors = 0;
                for (OperationLifetime? operation = m_currentOperation.Value; operation is not null;
                    operation = operation.Parent)
                {
                    if (operation.IsOwnedBy(this))
                    {
                        ancestors++;
                    }
                }
                if (m_publicationFinished is not null || m_activeOperations != ancestors)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadServerTooBusy, "Another lifecycle operation owns publication admission.");
                }
                if (!ReferenceEquals(revision.Owner, this))
                {
                    throw new InvalidOperationException("The publication revision belongs to another writer.");
                }
                var reservation = new PublicationReservation(this, revision.IsCurrent());
                m_publicationId = reservation.Id;
                m_publicationFinished = reservation.Finished;
                reservation.Operation = EnterOperation(reservation);
                return reservation;
            }
        }

        /// <summary>
        /// Excludes new operations after the admitted invocation ends. Never holds mutation ownership while
        /// waiting for invocation disposal or outstanding operations.
        /// </summary>
        internal async ValueTask<Task> BeginShutdownAsync(CancellationToken cancellationToken)
        {
            ThrowIfDecisionCallbackActive();
            while (true)
            {
                Task finished;
                lock (m_state)
                {
                    if (m_publicationFinished is null)
                    {
                        if (m_synchronizationDisposed)
                        {
                            throw new ObjectDisposedException(nameof(NodeManagerLifecycle));
                        }
                        m_shuttingDown = true;
                        m_activeShutdownMethods++;
                        return m_activeOperations == 0
                            ? Task.CompletedTask
                            : (m_operationsDrained ??= new TaskCompletionSource<bool>(
                                TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                    }
                    finished = m_publicationFinished;
                }
                await finished.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        internal async ValueTask<IDisposable> BeginShutdownCleanupAsync(CancellationToken cancellationToken)
        {
            await m_shutdownCleanup.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new ShutdownCleanup(m_shutdownCleanup);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            ThrowIfDecisionCallbackActive();
            lock (m_state)
            {
                m_disposed = true;
                m_shuttingDown = true;
                TryDisposeSynchronization();
            }
        }

        /// <summary>
        /// Rejects lifecycle entry from an active decision, including execution contexts spawned by it.
        /// </summary>
        internal void ThrowIfDecisionCallbackActive()
        {
            if (m_decision.Value is { IsActive: true })
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
            if (m_disposed && !m_synchronizationDisposed && m_activeOperations == 0 &&
                m_activeShutdownMethods == 0 && !m_shutdownPrepared && m_canDispose())
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
            if (host is not IDynamicNodeManagerBatchHost batchHost || server.Factory is not EncodeableFactory factory)
            {
                throw new NotSupportedException("The server cannot capture publication revisions.");
            }
            _ = server.TypeTree.CaptureSnapshot(out TypeTable types, out long typeRevision);
            _ = factory.CaptureSnapshot(out EncodeableFactory originalFactory, out long factoryRevision);
            return new PublicationRevision(
                this, server, host, batchHost, batchHost.RoutingRevision, types, typeRevision,
                originalFactory, factoryRevision);
        }

        /// <summary>
        /// Immutable revisions whose validity is checked before preparation has any effects.
        /// </summary>
        internal sealed record PublicationRevision(
            LifecycleWriter Owner,
            IServerInternal Server,
            IDynamicNodeManagerHost OwnerHost,
            IDynamicNodeManagerBatchHost Host,
            NodeManagerRoutingTable.RoutingSnapshot RoutingRevision,
            TypeTable Types,
            long TypeRevision,
            EncodeableFactory Factory,
            long FactoryRevision)
        {
            /// <summary>
            /// Checks routing, type and encodeable revisions together.
            /// </summary>
            internal bool IsCurrent()
            {
                _ = ((EncodeableFactory)Server.Factory).CaptureSnapshot(
                    out EncodeableFactory factory, out long factoryRevision);
                return ReferenceEquals(Host.RoutingRevision, RoutingRevision) &&
                    Server.TypeTree.IsCurrentSnapshot(Types, TypeRevision) &&
                    ReferenceEquals(factory, Factory) && factoryRevision == FactoryRevision;
            }
        }

        /// <summary>
        /// Transferable mutation ownership that can end without releasing operation admission.
        /// </summary>
        internal sealed class Mutation(LifecycleWriter owner) : IDisposable
        {
            /// <summary>
            /// Invokes the sole callback allowed under mutation ownership.
            /// </summary>
            internal async ValueTask DecideAsync(
                Func<CancellationToken, ValueTask> callback,
                CancellationToken cancellationToken)
            {
                owner.ThrowIfDecisionCallbackActive();
                if (!m_held)
                {
                    throw new InvalidOperationException("The durable decision requires mutation ownership.");
                }
                DecisionScope? previous = owner.m_decision.Value;
                var scope = new DecisionScope();
                owner.m_decision.Value = scope;
                try
                {
                    await callback(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    scope.Deactivate();
                    owner.m_decision.Value = previous;
                }
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

            /// <summary>Whether this owner currently holds mutation admission.</summary>
            private bool m_held = true;
        }

        /// <summary>
        /// Owns an invocation's admission, sequential preparation and prepared transaction disposal.
        /// </summary>
        internal sealed class PublicationReservation(LifecycleWriter owner, bool current) : IAsyncDisposable
        {
            /// <summary>
            /// Gets the unforgeable admission identity of this invocation.
            /// </summary>
            internal Guid Id { get; } = Guid.NewGuid();

            /// <summary>
            /// Gets whether all captured publication revisions were current when admitted.
            /// </summary>
            internal bool IsCurrent { get; } = current;

            /// <summary>
            /// Gets or sets the operation held until every prepared transaction has ended.
            /// </summary>
            internal OperationLifetime? Operation { get; set; }

            /// <summary>
            /// Gets completion of invocation disposal, including its transactions.
            /// </summary>
            internal Task Finished => m_finished.Task;

            /// <summary>
            /// Prepares one transaction while preventing overlapping preparation or premature disposal.
            /// </summary>
            /// <typeparam name="T">The prepared transaction type.</typeparam>
            internal async ValueTask<T> PrepareAsync<T>(Func<ValueTask<T>> prepare)
                where T : IAsyncDisposable
            {
                owner.ThrowIfDecisionCallbackActive();
                lock (owner.m_state)
                {
                    if (m_closing || owner.m_publicationId != Id || !IsCurrent)
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
                    T batch = await prepare().ConfigureAwait(false);
                    m_batches.Add(batch);
                    return batch;
                }
                finally
                {
                    lock (owner.m_state)
                    {
                        m_preparing = false;
                        m_prepared.TrySetResult(true);
                    }
                }
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                owner.ThrowIfDecisionCallbackActive();
                Task? prepared;
                bool ownsDisposal;
                lock (owner.m_state)
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
                finally
                {
                    lock (owner.m_state)
                    {
                        owner.m_publicationId = Guid.Empty;
                        owner.m_publicationFinished = null;
                    }
                    Operation?.Dispose();
                    m_finished.TrySetResult(true);
                }
            }

            /// <summary>Transactions retained until invocation disposal.</summary>
            private readonly List<IAsyncDisposable> m_batches = [];

            /// <summary>Completion shared by disposal and shutdown waiters.</summary>
            private readonly TaskCompletionSource<bool> m_finished =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Completion of the currently admitted preparation.</summary>
            private TaskCompletionSource<bool> m_prepared =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Whether a transaction is being prepared.</summary>
            private bool m_preparing;

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
            internal OperationLifetime(LifecycleWriter owner, OperationLifetime? parent)
            {
                m_owner = owner;
                Parent = parent;
            }

            /// <summary>
            /// Gets the parent operation in the originating execution context.
            /// </summary>
            internal OperationLifetime? Parent { get; }

            /// <summary>
            /// Determines whether the operation still belongs to a writer.
            /// </summary>
            internal bool IsOwnedBy(LifecycleWriter writer)
            {
                return ReferenceEquals(Volatile.Read(ref m_owner), writer);
            }

            /// <summary>
            /// Activates a transferred operation for nested invocation admission.
            /// </summary>
            internal IDisposable Activate()
            {
                LifecycleWriter writer = Volatile.Read(ref m_owner) ??
                    throw new ObjectDisposedException(nameof(OperationLifetime));
                OperationLifetime? previous = writer.m_currentOperation.Value;
                writer.m_currentOperation.Value = this;
                return new AmbientScope(writer, previous);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                LifecycleWriter? writer = Interlocked.Exchange(ref m_owner, null);
                if (writer is not null)
                {
                    if (ReferenceEquals(writer.m_currentOperation.Value, this))
                    {
                        writer.m_currentOperation.Value = Parent;
                    }
                    writer.ExitOperation();
                }
            }

            /// <summary>
            /// Restores operation context without ending the transferred operation.
            /// </summary>
            private sealed class AmbientScope(LifecycleWriter writer, OperationLifetime? previous) : IDisposable
            {
                /// <inheritdoc/>
                public void Dispose()
                {
                    writer.m_currentOperation.Value = previous;
                }
            }

            /// <summary>The writer until this operation ends.</summary>
            private LifecycleWriter? m_owner;
        }

        /// <summary>
        /// Releases independent shutdown-cleanup admission.
        /// </summary>
        private sealed class ShutdownCleanup(SemaphoreSlim semaphore) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// Shared decision state expires even in execution contexts spawned by the callback.
        /// </summary>
        private sealed class DecisionScope
        {
            /// <summary>
            /// Gets whether the decision is still executing.
            /// </summary>
            internal bool IsActive => Volatile.Read(ref m_active) != 0;

            /// <summary>
            /// Expires this decision in every captured execution context.
            /// </summary>
            internal void Deactivate()
            {
                Volatile.Write(ref m_active, 0);
            }

            /// <summary>Decision activity shared across execution contexts.</summary>
            private int m_active = 1;
        }

        /// <summary>Checks whether independent cleanup still requires shutdown synchronization.</summary>
        private readonly Func<bool> m_canDispose;

        /// <summary>Mutation serialization, never used by reclamation.</summary>
        private readonly SemaphoreSlim m_mutation = new(1, 1);

        /// <summary>Shutdown cleanup serialization, independent of the mutation writer.</summary>
        private readonly SemaphoreSlim m_shutdownCleanup = new(1, 1);

        /// <summary>Protects operation and invocation admission.</summary>
        private readonly Lock m_state = new();

        /// <summary>Active operation ancestry for reentrant invocation admission.</summary>
        private readonly AsyncLocal<OperationLifetime?> m_currentOperation = new();

        /// <summary>Active durable decision in the current execution context.</summary>
        private readonly AsyncLocal<DecisionScope?> m_decision = new();

        /// <summary>Completion of the sole caller-owned invocation, if any.</summary>
        private Task? m_publicationFinished;

        /// <summary>Identity of the sole invocation admitted by the writer.</summary>
        private Guid m_publicationId;

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
    }
}
