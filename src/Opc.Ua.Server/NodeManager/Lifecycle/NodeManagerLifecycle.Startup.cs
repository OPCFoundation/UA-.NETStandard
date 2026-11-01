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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public sealed partial class NodeManagerLifecycle
    {
        /// <summary>
        /// Transfers the host's exact stable system identities to the server's generation owner.
        /// </summary>
        internal void InitializeSystemSlots(IServerInternal server, IDynamicNodeManagerHost host)
        {
            foreach (NodeManagerSlot slot in host.SystemSlots)
            {
                NodeManagerGeneration generation = slot.Current ??
                    throw new InvalidOperationException("A system role has no initial generation.");
                var registration = new NodeManagerRegistration(slot.Id, generation.Number, generation.NodeManager);
                var prepared = new PreparedNodeManager(generation.NodeManager, [])
                {
                    Generation = generation,
                    Published = true
                };
                lock (m_registrationLock)
                {
                    m_registrations.Add(registration.Id, new RegistrationState(registration, prepared)
                    {
                        IsSystem = true,
                        ReadinessPending = NodeManagerReadiness.IsParticipant(generation.NodeManager)
                    });
                }
            }
        }

        /// <summary>
        /// Publishes initial additions through the normal pipeline before runtime components are bound.
        /// </summary>
        /// <param name="server">The exact server instance being initialized.</param>
        /// <param name="factories">The initial application factories in address-space initialization order.</param>
        /// <param name="cancellationToken">Cancels preparation or publication before the commit point.</param>
        /// <returns>The publication whose reconciliation must be completed or aborted by startup.</returns>
        internal async ValueTask<InitialPublication> BeginInitialPublicationAsync(
            IServerInternal server,
            ArrayOf<IAsyncNodeManagerFactory> factories,
            CancellationToken cancellationToken)
        {
            if (server.IsRunning)
            {
                throw new InvalidOperationException("An initial publication requires an unstarted server.");
            }
            if (!ReferenceEquals(server, m_server.CurrentInstance) ||
                server.NodeManager is not IDynamicNodeManagerHost host)
            {
                throw new InvalidOperationException("The initial publication does not belong to this server.");
            }
            var initial = new InitialPublication(this, server, host);
            await initial.StartAsync(
                factories.ToList().Select(NodeManagerBatchChange.Add).ToArrayOf(),
                cancellationToken).ConfigureAwait(false);
            return initial;
        }

        /// <summary>
        /// Revalidates an explicit bootstrap capability without admitting public runtime changes before Running.
        /// </summary>
        private void EnsurePublicationServer(
            IServerInternal server,
            IDynamicNodeManagerHost host,
            InitialPublication? initial,
            ShutdownPublication? shutdown = null)
        {
            if (shutdown is not null)
            {
                shutdown.Validate(this, server, host);
                return;
            }
            if (initial is null)
            {
                EnsureSameRunningServer(server, host);
                return;
            }
            initial.Validate(this, server, host);
        }

        /// <summary>
        /// Owns one bootstrap commit through publication and the later runtime/readiness boundary.
        /// </summary>
        internal sealed class InitialPublication
        {
            /// <summary>
            /// Captures the only server and writer that may use this bootstrap capability.
            /// </summary>
            internal InitialPublication(
                NodeManagerLifecycle owner,
                IServerInternal server,
                IDynamicNodeManagerHost host)
            {
                m_owner = owner;
                m_server = server;
                m_host = host;
                lock (owner.m_registrationLock)
                {
                    SystemRegistrations = owner.m_registrations.Values.Where(state => state.IsSystem)
                        .Select(state => state.Registration).ToArrayOf();
                }
            }

            /// <summary>
            /// Gets system identities reconciled by the same initial batch as the application additions.
            /// </summary>
            internal ArrayOf<NodeManagerRegistration> SystemRegistrations { get; }

            /// <summary>
            /// Prepares an ordinary Add batch and returns once its view has been published.
            /// </summary>
            internal async ValueTask StartAsync(
                ArrayOf<NodeManagerBatchChange> changes,
                CancellationToken cancellationToken)
            {
                IPreparedNodeManagerBatch batch = await m_owner.PrepareBatchAsync(
                    changes,
                    null,
                    m_owner.CaptureLifecycleCallbackPermit(),
                    cancellationToken,
                    initial: this).ConfigureAwait(false);
                m_completion = CommitAsync(batch, cancellationToken);
                try
                {
                    Task completed = await Task.WhenAny(m_published.Task, m_completion)
                        .WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (ReferenceEquals(completed, m_completion))
                    {
                        await m_completion.ConfigureAwait(false);
                        throw new InvalidOperationException("The initial batch finished without publishing its view.");
                    }
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    Exception? cleanup = await AbortAsync().ConfigureAwait(false);
                    if (cleanup is not null && !ReferenceEquals(cleanup, failure))
                    {
                        throw new AggregateException("Initial publication and cleanup failed.", failure, cleanup);
                    }
                    throw;
                }
            }

            /// <summary>
            /// Releases normal reconciliation only after sessions, events and subscriptions have been initialized.
            /// </summary>
            internal async ValueTask CompleteAsync()
            {
                m_runtimeReady.TrySetResult(true);
                NodeManagerBatchResult result = await Completion.ConfigureAwait(false);
                if (result.CleanupFailure is { } failure)
                {
                    throw new InvalidOperationException("Initial generation reconciliation failed.", failure);
                }
            }

            /// <summary>
            /// Releases operation ownership when later startup fails, without aborting a published generation.
            /// </summary>
            /// <returns>A non-cancellation failure that the startup caller must surface, or null.</returns>
            internal async ValueTask<Exception?> AbortAsync()
            {
                m_runtimeReady.TrySetCanceled();
                try
                {
                    await Completion.ConfigureAwait(false);
                    return null;
                }
                catch (OperationCanceledException) when (m_runtimeReady.Task.IsCanceled)
                {
                    return null;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    return failure;
                }
            }

            /// <summary>
            /// Waits for startup's explicit runtime boundary; no ambient startup exemption flows to user code.
            /// </summary>
            internal Task WaitForRuntimeAsync()
            {
                return m_runtimeReady.Task;
            }

            /// <summary>
            /// Gets the exact unstarted server without broadening the public lifecycle admission policy.
            /// </summary>
            internal (IServerInternal Server, IDynamicNodeManagerHost Host) GetServer()
            {
                Validate(m_owner, m_server, m_host);
                return (m_server, m_host);
            }

            /// <summary>
            /// Checks ownership and server identity before each preparation or publication continuation.
            /// </summary>
            internal void Validate(
                NodeManagerLifecycle owner,
                IServerInternal server,
                IDynamicNodeManagerHost host)
            {
                m_owner.m_writer.EnsureRunning();
                if (!ReferenceEquals(owner, m_owner) ||
                    !ReferenceEquals(server, m_server) ||
                    !ReferenceEquals(host, m_host) ||
                    !ReferenceEquals(m_owner.m_server.CurrentInstance, server) ||
                    !ReferenceEquals(server.NodeManager, host))
                {
                    throw new InvalidOperationException("The server changed during its initial publication.");
                }
            }

            /// <summary>
            /// Runs the same commit/readiness path as runtime additions, retaining batch disposal ownership.
            /// </summary>
            private async Task<NodeManagerBatchResult> CommitAsync(
                IPreparedNodeManagerBatch batch,
                CancellationToken cancellationToken)
            {
                await using var lifetime = batch.ConfigureAwait(false);
                return await batch.CommitAsync(
                    static _ => default,
                    () => m_published.TrySetResult(true),
                    cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Gets the commit task after preparation has transferred its operation ownership.
            /// </summary>
            private Task<NodeManagerBatchResult> Completion =>
                m_completion ?? throw new InvalidOperationException("Initial preparation has not completed.");

            /// <summary>The lifecycle that owns the commit operation.</summary>
            private readonly NodeManagerLifecycle m_owner;

            /// <summary>The server being bootstrapped.</summary>
            private readonly IServerInternal m_server;

            /// <summary>The address-space host installed for this startup.</summary>
            private readonly IDynamicNodeManagerHost m_host;

            /// <summary>The view-publication milestone, independent of readiness completion.</summary>
            private readonly TaskCompletionSource<bool> m_published =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The runtime-component boundary controlled by server startup.</summary>
            private readonly TaskCompletionSource<bool> m_runtimeReady =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The owned canonical commit and reconciliation task.</summary>
            private Task<NodeManagerBatchResult>? m_completion;
        }
    }
}
