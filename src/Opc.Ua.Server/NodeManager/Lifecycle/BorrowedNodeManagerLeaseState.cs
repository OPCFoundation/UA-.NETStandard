/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Serializes candidate lease cleanup and retains successful cleanup across callers.
    /// </summary>
    internal sealed class BorrowedNodeManagerLeaseState
    {
        /// <summary>
        /// Retains the lease returned by the factory.
        /// </summary>
        /// <param name="lease">The candidate cleanup lease.</param>
        /// <param name="server">The candidate's staging server.</param>
        internal BorrowedNodeManagerLeaseState(
            INodeManagerBorrowedResourceLease lease,
            ChangePreparer.StagingServer server)
        {
            m_lease = lease ?? throw new ArgumentNullException(nameof(lease));
            m_server = server ?? throw new ArgumentNullException(nameof(server));
        }

        /// <summary>
        /// Gets the provider's lease.
        /// </summary>
        internal INodeManagerBorrowedResourceLease Lease => m_lease;

        /// <summary>
        /// Validates cleanup ownership before the routing publication.
        /// </summary>
        internal void ValidateForPublication()
        {
            lock (m_sync)
            {
                if (m_published || m_rollback is not null)
                {
                    throw new InvalidOperationException("The candidate lease is no longer available for publication.");
                }
            }
        }

        /// <summary>
        /// Reserves publication before the durable decision and prevents rollback from starting.
        /// </summary>
        internal PublicationReservation ReservePublication()
        {
            lock (m_sync)
            {
                if (m_published || m_rollback is not null || m_publicationDecision is not null)
                {
                    throw new InvalidOperationException("The candidate lease cannot reserve publication.");
                }
                m_publicationDecision = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                return new PublicationReservation(this, m_publicationDecision);
            }
        }

        /// <summary>
        /// Transfers cleanup from rollback to retirement after the routing publication.
        /// </summary>
        internal void MarkPublished()
        {
            lock (m_sync)
            {
                if (m_published)
                {
                    return;
                }
                if (m_rollback is not null || m_publicationDecision is not null)
                {
                    throw new InvalidOperationException("The candidate lease has another publication or rollback owner.");
                }
                m_published = true;
            }
        }

        /// <summary>
        /// Reverses private preparation, sharing an in-progress attempt and retrying a failed attempt.
        /// </summary>
        /// <returns>A task that completes after rollback.</returns>
        internal ValueTask RollbackAsync()
        {
            TaskCompletionSource<bool>? completion = null;
            Task? attempt = null;
            Task<bool>? publicationDecision = null;
            lock (m_sync)
            {
                if (m_published)
                {
                    throw new InvalidOperationException("A published candidate cannot be rolled back.");
                }
                if (m_publicationDecision is not null)
                {
                    publicationDecision = m_publicationDecision.Task;
                }
                else
                {
                    if (m_rollback is null || m_rollback.IsFaulted || m_rollback.IsCanceled)
                    {
                        completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        m_rollback = completion.Task;
                    }
                    attempt = m_rollback;
                }
            }
            if (publicationDecision is not null)
            {
                return new ValueTask(RollbackAfterPublicationDecisionAsync(publicationDecision));
            }
            if (completion is not null)
            {
                _ = CompleteAsync(rollback: true, completion);
            }
            return new ValueTask(attempt!);
        }

        /// <summary>
        /// Records the candidate whose source must be released after private cleanup.
        /// </summary>
        /// <param name="candidate">The facade returned by the lease.</param>
        internal void SetCandidate(IAsyncNodeManager candidate)
        {
            m_candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        }

        /// <summary>
        /// Releases a published candidate, sharing an in-progress attempt and retrying a failed attempt.
        /// </summary>
        /// <returns>A task that completes after retirement cleanup.</returns>
        internal ValueTask RetireAsync()
        {
            TaskCompletionSource<bool>? completion = null;
            Task attempt;
            lock (m_sync)
            {
                if (!m_published)
                {
                    throw new InvalidOperationException("An unpublished candidate cannot be retired.");
                }
                if (m_retirement is null || m_retirement.IsFaulted || m_retirement.IsCanceled)
                {
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_retirement = completion.Task;
                }
                attempt = m_retirement;
            }
            if (completion is not null)
            {
                _ = CompleteAsync(rollback: false, completion);
            }
            return new ValueTask(attempt);
        }

        /// <summary>
        /// Continues a rollback after its competing publication commits or is abandoned.
        /// </summary>
        private async Task RollbackAfterPublicationDecisionAsync(Task<bool> publicationDecision)
        {
            if (await publicationDecision.ConfigureAwait(false))
            {
                throw new InvalidOperationException("A published candidate cannot be rolled back.");
            }
            await RollbackAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Commits the lease reservation after the routing view is published.
        /// </summary>
        private void CommitPublication(TaskCompletionSource<bool> decision)
        {
            lock (m_sync)
            {
                if (!ReferenceEquals(m_publicationDecision, decision))
                {
                    throw new InvalidOperationException("The candidate publication reservation is no longer active.");
                }
                m_published = true;
                m_publicationDecision = null;
            }
            decision.TrySetResult(true);
        }

        /// <summary>
        /// Releases only the matching unpublished reservation.
        /// </summary>
        private void CancelPublication(TaskCompletionSource<bool> decision)
        {
            bool canceled;
            lock (m_sync)
            {
                canceled = ReferenceEquals(m_publicationDecision, decision);
                if (canceled)
                {
                    m_publicationDecision = null;
                }
            }
            if (canceled)
            {
                decision.TrySetResult(false);
            }
        }

        /// <summary>
        /// Completes one shared cleanup attempt.
        /// </summary>
        /// <param name="rollback">Whether the candidate remains unpublished.</param>
        /// <param name="completion">The task shared by concurrent cleanup callers.</param>
        private async Task CompleteAsync(
            bool rollback,
            TaskCompletionSource<bool> completion)
        {
            try
            {
                if (rollback)
                {
                    if (!m_providerRollbackCompleted)
                    {
                        await m_lease.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                        m_providerRollbackCompleted = true;
                    }
                    m_server.Detach();
                    await m_server.DisposeAsync().ConfigureAwait(false);
                    if (m_candidate is not null &&
                        ChangePreparer.IsBorrowedCandidate(m_candidate) &&
                        NodeManagerGenerationRegistry.TryGet(m_candidate, out NodeManagerGeneration? generation) &&
                        generation is not null &&
                        generation.Gate.State == NodeManagerGenerationState.Aborted)
                    {
                        generation.ReleaseForwardedSource();
                    }
                }
                else
                {
                    await m_lease.RetireAsync(CancellationToken.None).ConfigureAwait(false);
                }
                completion.TrySetResult(true);
            }
            catch (Exception failure)
            {
                completion.TrySetException(!rollback || failure is OutOfMemoryException
                    ? failure
                    : new NodeManagerBorrowedResourceCleanupException(this, rollback, failure));
            }
        }

        /// <summary>
        /// Owns one publication decision until it commits or is abandoned.
        /// </summary>
        internal sealed class PublicationReservation : IDisposable
        {
            /// <summary>
            /// Captures the lease and the reserved decision.
            /// </summary>
            internal PublicationReservation(
                BorrowedNodeManagerLeaseState owner,
                TaskCompletionSource<bool> decision)
            {
                m_owner = owner;
                m_decision = decision;
            }

            /// <summary>
            /// Commits the reserved publication.
            /// </summary>
            internal void Commit()
            {
                BorrowedNodeManagerLeaseState owner = Interlocked.Exchange(ref m_owner, null) ??
                    throw new InvalidOperationException("The candidate publication reservation was already consumed.");
                owner.CommitPublication(m_decision);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Exchange(ref m_owner, null)?.CancelPublication(m_decision);
            }

            private BorrowedNodeManagerLeaseState? m_owner;
            private readonly TaskCompletionSource<bool> m_decision;
        }

        /// <summary>The provider's candidate cleanup callbacks.</summary>
        private readonly INodeManagerBorrowedResourceLease m_lease;

        /// <summary>The candidate whose cleanup is owned by this lease.</summary>
        private IAsyncNodeManager? m_candidate;

        /// <summary>The staging server retained until rollback cleanup succeeds.</summary>
        private readonly ChangePreparer.StagingServer m_server;

        /// <summary>Protects publication and cleanup attempts.</summary>
        private readonly Lock m_sync = new();

        /// <summary>The current or completed rollback attempt.</summary>
        private Task? m_rollback;

        /// <summary>The current or completed retirement attempt.</summary>
        private Task? m_retirement;

        /// <summary>The reserved publication's pending commit or abandonment decision.</summary>
        private TaskCompletionSource<bool>? m_publicationDecision;

        /// <summary>Whether routing publication transferred cleanup to retirement.</summary>
        private bool m_published;

        /// <summary>Whether provider rollback completed before staging-server cleanup.</summary>
        private bool m_providerRollbackCompleted;
    }
}
