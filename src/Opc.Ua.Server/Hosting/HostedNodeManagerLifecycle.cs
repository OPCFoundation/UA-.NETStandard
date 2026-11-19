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

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Forwards <see cref="INodeManagerLifecycle"/> calls from dependency injection to the
    /// server instance that is currently running in the host. The provider is registered as a
    /// singleton before a server exists, so it stays valid across host restarts and reports a
    /// clear error while no server is attached.
    /// </summary>
    internal sealed class HostedNodeManagerLifecycle : INodeManagerLifecycle
    {
        /// <summary>
        /// Creates a host lifecycle with recovery unavailable unless trusted adapters are supplied.
        /// </summary>
        internal HostedNodeManagerLifecycle()
            : this(new NodeManagerCleanupAuthority(null, null, null))
        {
        }

        /// <summary>
        /// Creates a host lifecycle using the adapters chosen by its composition root.
        /// </summary>
        internal HostedNodeManagerLifecycle(NodeManagerCleanupAuthority cleanupAuthority)
        {
            CleanupAuthority = cleanupAuthority ?? throw new ArgumentNullException(nameof(cleanupAuthority));
        }

        /// <summary>
        /// Gets the single recovery authority shared with every server attached to this host.
        /// </summary>
        internal NodeManagerCleanupAuthority CleanupAuthority { get; }

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
        public ILifecycleCapture CaptureChanges()
        {
            return Current.CaptureChanges();
        }

        /// <summary>
        /// Resolves the hosted lifecycle before capturing the initializer's exact publication.
        /// </summary>
        /// <exception cref="NotSupportedException">The host does not support readiness-bound capture.</exception>
        internal ILifecycleCapture CaptureChanges(NodeManagerReadinessScope readiness)
        {
            return Current is NodeManagerLifecycle lifecycle
                ? lifecycle.CaptureChanges(readiness, bindReadiness: true)
                : throw new NotSupportedException("The hosted lifecycle does not support readiness-bound capture.");
        }

        /// <inheritdoc/>
        public ValueTask<IPreparedChanges> PrepareAsync(
            ArrayOf<LifecycleChange> changes,
            CancellationToken cancellationToken = default)
        {
            return Current.PrepareAsync(changes, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<IPreparedChanges> PrepareAsync(
            ArrayOf<LifecycleChange> changes,
            NodeManagerDecisionContext decisionContext,
            CancellationToken cancellationToken)
        {
            return Current.PrepareAsync(changes, decisionContext, cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask<IPreparedChanges> PrepareFromCallbackAsync(
            ArrayOf<LifecycleChange> changes,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            return Current.PrepareFromCallbackAsync(changes, context, cancellationToken);
        }

        /// <summary>
        /// Attaches the lifecycle of the server that just started, so injected consumers are
        /// routed to it.
        /// </summary>
        /// <param name="lifecycle">The lifecycle of the running server.</param>
        /// <param name="resumeShutdown">The server's asynchronous shutdown continuation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifecycle"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">Another server is already attached.</exception>
        internal void Attach(INodeManagerLifecycle lifecycle, Func<ValueTask>? resumeShutdown = null)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            lock (m_recoveryLock)
            {
                INodeManagerLifecycle? current = Volatile.Read(ref m_current);
                if (current is not null && !ReferenceEquals(current, lifecycle))
                {
                    throw new InvalidOperationException(
                        "An OPC UA server is already attached to this lifecycle provider.");
                }
                if (lifecycle is NodeManagerLifecycle lifecycleOwner)
                {
                    lifecycleOwner.BindCleanupAuthority(CleanupAuthority);
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
                if (lifecycle is NodeManagerLifecycle owner)
                {
                    RecoveryOwner? entry = m_recoveryOwners.Find(item => ReferenceEquals(item.Owner, owner));
                    if (entry is null)
                    {
                        m_recoveryOwners.Add(new RecoveryOwner(owner, resumeShutdown));
                    }
                    else
                    {
                        entry.Detached = false;
                        entry.ResumeShutdown = resumeShutdown ?? entry.ResumeShutdown;
                    }
                }
            }
        }

        /// <summary>
        /// Detaches the lifecycle of a stopping server. Detaching a lifecycle that is no longer
        /// attached is ignored, so shutdown stays idempotent.
        /// </summary>
        /// <param name="lifecycle">The lifecycle of the stopping server.</param>
        /// <param name="disposed">Whether the server completed disposal.</param>
        /// <exception cref="ArgumentNullException"><paramref name="lifecycle"/> is <c>null</c>.</exception>
        internal void Detach(INodeManagerLifecycle lifecycle, bool disposed = false)
        {
            if (lifecycle is null)
            {
                throw new ArgumentNullException(nameof(lifecycle));
            }

            lock (m_recoveryLock)
            {
                Interlocked.CompareExchange(ref m_current, null, lifecycle);
                if (lifecycle is NodeManagerLifecycle owner)
                {
                    int index = m_recoveryOwners.FindIndex(entry => ReferenceEquals(entry.Owner, owner));
                    if (index >= 0)
                    {
                        if (disposed)
                        {
                            m_recoveryOwners.RemoveAt(index);
                        }
                        else
                        {
                            m_recoveryOwners[index].Detached = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Captures the attached and unsuccessfully detached lifecycle owners.
        /// </summary>
        /// <returns>The owners still reachable for cleanup recovery.</returns>
        internal NodeManagerLifecycle[] GetRecoveryOwners()
        {
            lock (m_recoveryLock)
            {
                return m_recoveryOwners.Select(entry => entry.Owner).ToArray();
            }
        }

        /// <summary>
        /// Resumes normal retirement or the detached server's ordered asynchronous shutdown.
        /// </summary>
        /// <param name="owner">The lifecycle whose resource failure was confirmed.</param>
        /// <returns>A task completed after shutdown resumption is attempted.</returns>
        /// <exception cref="InvalidOperationException">
        /// The lifecycle is no longer retained by this host, or its ordered shutdown cannot be resumed.
        /// </exception>
        internal async ValueTask ResumeCleanupAsync(NodeManagerLifecycle owner)
        {
            RecoveryOwner entry;
            bool detached;
            lock (m_recoveryLock)
            {
                entry = m_recoveryOwners.FirstOrDefault(item => ReferenceEquals(item.Owner, owner))
                    ?? throw new InvalidOperationException("The lifecycle is no longer retained by this host.");
                detached = entry.Detached;
            }
            if (!detached && owner.TryRetryCleanupAfterConfirmation())
            {
                lock (m_recoveryLock)
                {
                    if (!m_recoveryOwners.Contains(entry))
                    {
                        return;
                    }
                    detached = entry.Detached;
                }
                if (!detached && !owner.IsShuttingDown)
                {
                    return;
                }
            }
            Func<ValueTask> resume = entry.ResumeShutdown
                ?? throw new InvalidOperationException("The host cannot resume this server's ordered shutdown.");
            await resume().ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the lifecycle of the attached server.
        /// </summary>
        /// <exception cref="InvalidOperationException">No server is attached.</exception>
        private INodeManagerLifecycle Current
            => Volatile.Read(ref m_current)
                ?? throw new InvalidOperationException(
                    "The hosted OPC UA server is not running.");

        /// <summary>
        /// Retains a detached lifecycle and its server shutdown continuation.
        /// </summary>
        private sealed class RecoveryOwner
        {
            /// <summary>
            /// Creates an owner record.
            /// </summary>
            internal RecoveryOwner(NodeManagerLifecycle owner, Func<ValueTask>? resumeShutdown)
            {
                Owner = owner;
                ResumeShutdown = resumeShutdown;
            }

            /// <summary>
            /// Gets the lifecycle retaining the generations.
            /// </summary>
            internal NodeManagerLifecycle Owner { get; }

            /// <summary>
            /// Gets the server's ordered shutdown continuation.
            /// </summary>
            internal Func<ValueTask>? ResumeShutdown { get; set; }

            /// <summary>
            /// Gets or sets whether the lifecycle has been detached.
            /// </summary>
            internal bool Detached { get; set; }
        }

        private INodeManagerLifecycle? m_current;
        private readonly Lock m_recoveryLock = new();
        private readonly List<RecoveryOwner> m_recoveryOwners = [];
    }
}
