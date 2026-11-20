/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Forwards <see cref="INodeManagerLifecycle"/> calls from dependency injection to the
    /// server instance that is currently running in the host. The provider is registered as a
    /// singleton before a server exists, so it stays valid across host restarts and reports a
    /// clear error while no server is attached.
    /// </summary>
    internal sealed class HostedNodeManagerLifecycle : INodeManagerPublicationLifecycle
    {
        /// <inheritdoc/>
        public long Revision
            => Current.Revision;

        /// <inheritdoc/>
        public ArrayOf<NodeManagerSlot> Slots
            => Current.Slots;

        /// <inheritdoc/>
        public ArrayOf<NodeManagerRegistration> Registrations
            => Current.Registrations;

        /// <inheritdoc/>
        public bool IsShuttingDown
            => Current.IsShuttingDown;

        /// <inheritdoc/>
        public bool SupportsPreparedPublication
            => Current.SupportsPreparedPublication;

        /// <inheritdoc/>
        public bool SupportsPublicationIsolation
            => Current.SupportsPublicationIsolation;

        /// <inheritdoc/>
        public INodeManagerPublicationCapture CapturePublication()
            => Current.CapturePublication();

        /// <inheritdoc/>
        public ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default)
        {
            return Current.PrepareAsync(changes, cancellationToken);
        }

        /// <summary>
        /// Attaches the lifecycle of the server that just started, so injected consumers are
        /// routed to it.
        /// </summary>
        /// <param name="lifecycle">The lifecycle of the running server.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifecycle"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">Another server is already attached.</exception>
        internal void Attach(INodeManagerLifecycle lifecycle)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            INodeManagerLifecycle? previous = Interlocked.CompareExchange(
                ref m_current,
                lifecycle,
                null);
            if (previous is not null && !ReferenceEquals(previous, lifecycle))
            {
                throw new InvalidOperationException(
                    "An OPC UA server is already attached to this lifecycle provider.");
            }
        }

        /// <summary>
        /// Detaches the lifecycle of a stopping server. Detaching a lifecycle that is no longer
        /// attached is ignored, so shutdown stays idempotent.
        /// </summary>
        /// <param name="lifecycle">The lifecycle of the stopping server.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifecycle"/> is <c>null</c>.</exception>
        internal void Detach(INodeManagerLifecycle lifecycle)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            Interlocked.CompareExchange(ref m_current, null, lifecycle);
        }

        /// <summary>
        /// Gets the lifecycle of the attached server.
        /// </summary>
        /// <exception cref="InvalidOperationException">No server is attached.</exception>
        private INodeManagerLifecycle Current
            => Volatile.Read(ref m_current)
                ?? throw new InvalidOperationException(
                    "The hosted OPC UA server is not running.");

        private INodeManagerLifecycle? m_current;
    }
}
