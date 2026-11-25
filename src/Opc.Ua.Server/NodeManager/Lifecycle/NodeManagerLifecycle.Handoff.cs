// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// First-party factory that prepares an inert successor for an exact serving predecessor.
    /// </summary>
    internal interface INodeManagerHandoffFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a private successor and binds its first-party drain plan.
        /// </summary>
        ValueTask<NodeManagerHandoffCandidate> PrepareReplacementAsync(
            IServerInternal stagingServer,
            ApplicationConfiguration configuration,
            IAsyncNodeManager predecessor,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// An unpublished manager and the plan for pausing its exact predecessor at commit.
    /// </summary>
    internal sealed record NodeManagerHandoffCandidate(
        IAsyncNodeManager Manager,
        Func<INodeManagerHandoffPause> PausePredecessor);

    /// <summary>
    /// A device-local pause owned by one commit invocation.
    /// </summary>
    internal interface INodeManagerHandoffPause
    {
        /// <summary>
        /// Drains admitted calls and deferred effects, and rejects nontransferable pending state.
        /// </summary>
        ValueTask DrainAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Ends this pause. Reopening is permitted only for the exact pause owner.
        /// </summary>
        void Finish(bool reopen);
    }

    public sealed partial class NodeManagerLifecycle
    {
        /// <summary>
        /// Submits one in-memory handoff for the specified generation. Acceptance is not completion.
        /// The worker never runs inline or inherits request callback context.
        /// </summary>
        internal StatusCode QueueHandoff(
            NodeManagerRegistration predecessor,
            INodeManagerHandoffFactory factory,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref m_handoffQueued, 1, 0) != 0)
            {
                return StatusCodes.BadWouldBlock;
            }

            LifecycleWriter.OperationLifetime? operation = null;
            bool accepted = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation = EnterLifecycleOperation();
                (IServerInternal server, IDynamicNodeManagerHost host) = GetRunningServer(null);
                if (host is not MasterNodeManager)
                {
                    return StatusCodes.BadNotSupported;
                }
                if (!IsSelected(host, predecessor.Identity))
                {
                    return StatusCodes.BadInvalidState;
                }
                var request = new HandoffRequest(this, server, predecessor, factory, operation, cancellationToken);
                accepted = ThreadPool.UnsafeQueueUserWorkItem(
                    static state => _ = ((HandoffRequest)state!).RunAsync(), request);
                if (accepted)
                {
                    operation = null;
                }
                return accepted ? StatusCodes.Good : StatusCodes.BadResourceUnavailable;
            }
            finally
            {
                operation?.Dispose();
                if (!accepted)
                {
                    Volatile.Write(ref m_handoffQueued, 0);
                }
            }
        }

        /// <summary>
        /// Checks the published image, not the slot's preliminary publication state or an old pinned view.
        /// </summary>
        internal static bool IsSelected(IDynamicNodeManagerHost host, NodeManagerGeneration generation)
        {
            NodeManagerRoutingTable.RoutingSnapshot image = host.RoutingRevision;
            return Array.Exists(image.VisibleNodeManagers, manager => ReferenceEquals(manager, generation.NodeManager)) &&
                image.TryGetGeneration(generation.NodeManager, out NodeManagerGeneration? published) &&
                ReferenceEquals(published, generation);
        }

        /// <summary>
        /// Holds the exact predecessor pause across publication, but not across candidate cleanup.
        /// </summary>
        private async ValueTask<LifecycleResult?> CompleteHandoffAsync(
            PreparationPayload batch, CancellationToken cancellationToken)
        {
            EnsureSameRunningServer(batch.Server, batch.Host, null);
            BatchEntry entry = batch.Entries[0];
            NodeManagerGeneration predecessor = entry.Current!.Registration.Identity;
            if (!ReferenceEquals(batch.Host.RoutingRevision, batch.RoutingRevision) ||
                !IsSelected(batch.Host, predecessor) || !IsCurrentRegistration(entry.Current.Registration))
            {
                throw new NodeManagerRoutingConflictException(
                    "The serving predecessor changed before the handoff could drain.", batch.RoutingRevision);
            }
            cancellationToken.ThrowIfCancellationRequested();
            INodeManagerHandoffPause pause = entry.Handoff!.PausePredecessor();
            try
            {
                await pause.DrainAsync(cancellationToken).ConfigureAwait(false);
                return await CompleteChangesAsync(batch, null, false, null, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                pause.Finish(!batch.PublicationProgress.CorePublicationStarted &&
                    !batch.IsCommitted && !m_writer.IsShuttingDown && IsSelected(batch.Host, predecessor));
            }
        }

        /// <summary>
        /// Runs the one accepted replacement and keeps all completion and commit authority off the submitter's path.
        /// </summary>
        private sealed class HandoffRequest(
            NodeManagerLifecycle owner,
            IServerInternal server,
            NodeManagerRegistration predecessor,
            INodeManagerHandoffFactory factory,
            LifecycleWriter.OperationLifetime operation,
            CancellationToken cancellationToken)
        {
            /// <summary>
            /// Gets the exact predecessor named at acceptance.
            /// </summary>
            public NodeManagerRegistration Predecessor { get; } = predecessor;

            /// <summary>
            /// Prepares and commits with shutdown linked to the accepted lifecycle operation.
            /// </summary>
            public async Task RunAsync()
            {
                using (operation)
                using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                using (var finished = new CancellationTokenSource())
                {
                    Task monitor = CancelAtShutdownAsync(stop, operation.ShutdownStarted, finished.Token);
                    bool committed = false;
                    try
                    {
                        var prepared = (PreparedChanges)await owner.PrepareChangesAsync(
                            [LifecycleChange.Replace(Predecessor.Slot, factory)],
                            null, null, stop.Token, handoff: this).ConfigureAwait(false);
                        try
                        {
                            LifecycleResult result = await prepared.CommitHandoffAsync(this, stop.Token)
                                .ConfigureAwait(false);
                            committed = true;
                            if (result.CleanupFailure is { } failure)
                            {
                                server.Telemetry.CreateLogger<NodeManagerLifecycle>()
                                    .HandoffReportedFailure(true, failure.GetType().Name);
                            }
                        }
                        finally
                        {
                            await prepared.DisposeHandoffAsync(this).ConfigureAwait(false);
                        }
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        server.Telemetry.CreateLogger<NodeManagerLifecycle>()
                            .HandoffReportedFailure(committed, failure.GetType().Name);
                    }
                    finally
                    {
                        finished.Cancel();
                        try
                        {
                            await monitor.ConfigureAwait(false);
                        }
                        finally
                        {
                            Volatile.Write(ref owner.m_handoffQueued, 0);
                        }
                    }
                }
            }

            /// <summary>
            /// Cancels precommit on shutdown without executing cancellation callbacks under the writer lock.
            /// </summary>
            private async Task CancelAtShutdownAsync(
                CancellationTokenSource stop,
                Task shutdown,
                CancellationToken finished)
            {
                try
                {
                    await shutdown.WaitAsync(finished).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (finished.IsCancellationRequested)
                {
                    return;
                }
                try
                {
                    stop.Cancel();
                }
                catch (AggregateException failure)
                {
                    server.Telemetry.CreateLogger<NodeManagerLifecycle>()
                        .HandoffShutdownCancellationFailed(failure.GetType().Name);
                }
            }
        }

        /// <summary>Whether the host worker has an accepted handoff in progress.</summary>
        private int m_handoffQueued;
    }
}
