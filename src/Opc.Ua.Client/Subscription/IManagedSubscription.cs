/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Internal lifecycle contract used by the subscription manager to coordinate physical subscriptions.
    /// </summary>
    internal interface IManagedSubscription : ISubscription, IMessageProcessor
    {
        /// <summary>
        /// Gets whether a CreateSubscription request is awaiting its server identifier.
        /// </summary>
        /// <value>
        /// <c>true</c> while creation is in progress; otherwise, <c>false</c>.
        /// </value>
        bool IsCreationInProgress { get; }

        /// <summary>
        /// Gets whether intentional deletion retired this subscription's publish demand.
        /// Recovery resets do not retire demand.
        /// </summary>
        bool IsIntentionallyDeleted { get; }

        /// <summary>
        /// Completes a successful server transfer by synchronizing monitored-item handles and recovering notifications.
        /// </summary>
        /// <param name="availableSequenceNumbers">
        /// Sequence numbers of notification messages retained in the server's retransmission queue.
        /// </param>
        /// <param name="ct">The token used to cancel synchronization and notification recovery.</param>
        /// <returns>
        /// <c>true</c> if local transfer completion succeeds; <c>false</c> if monitored-item handles cannot
        /// be synchronized.
        /// </returns>
        ValueTask<bool> TryCompleteTransferAsync(
            IReadOnlyList<uint> availableSequenceNumbers,
            CancellationToken ct = default);

        /// <summary>
        /// Notifies the subscription that the subscription manager has paused or resumed publishing.
        /// </summary>
        /// <param name="paused">
        /// <c>true</c> when publishing is paused; <c>false</c> when publishing resumes.
        /// </param>
        void NotifySubscriptionManagerPaused(bool paused);
    }
}
