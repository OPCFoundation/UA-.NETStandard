/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional async extension of <see cref="IPushConfigurationTransactionCoordinator"/>
    /// that reserves cross-replica ownership of the single server-wide
    /// PushManagement transaction before a synchronous
    /// <see cref="IPushConfigurationTransactionCoordinator.Stage"/> call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default per-server coordinator does not implement this interface:
    /// its transaction is process-local, so the synchronous
    /// <see cref="IPushConfigurationTransactionCoordinator.ValidateSessionCanParticipate"/>
    /// check is sufficient and no additional asynchronous work is required.
    /// A distributed (high-availability) coordinator that shares transaction
    /// ownership across a redundant server set implements this seam so it can
    /// acquire or renew a shared lease at an <see langword="await"/> boundary,
    /// keeping the coordinator's synchronous <c>Stage</c>/<c>Cancel</c>
    /// contract free of any sync-over-async call.
    /// </para>
    /// <para>
    /// <see cref="ConfigurationNodeManager"/> and <see cref="TrustList"/> call
    /// <see cref="AcquireTransactionOwnershipAsync"/> immediately before every
    /// operation that stages a change; a coordinator that does not implement
    /// the interface simply skips the call, leaving the non-distributed server
    /// behaviour unchanged.
    /// </para>
    /// </remarks>
    public interface IPushConfigurationTransactionOwnershipGate
    {
        /// <summary>
        /// Reserves (acquires or renews) cross-replica ownership of the
        /// server-wide PushManagement transaction for
        /// <paramref name="sessionId"/> so that the subsequent synchronous
        /// <see cref="IPushConfigurationTransactionCoordinator.Stage"/> starts
        /// or continues the transaction on this replica only.
        /// </summary>
        /// <param name="sessionId">
        /// The Session that is about to stage an operation.
        /// </param>
        /// <param name="cancellationToken">A token used to cancel the wait.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadTransactionPending"/> when
        /// another replica currently owns the active transaction.
        /// </exception>
        ValueTask AcquireTransactionOwnershipAsync(
            NodeId sessionId,
            CancellationToken cancellationToken = default);
    }
}
