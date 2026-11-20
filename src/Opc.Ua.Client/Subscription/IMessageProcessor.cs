/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// A subscription component that processes received messages
    /// and dispatches to the subscribers.
    /// </summary>
    internal interface IMessageProcessor
    {
        /// <summary>
        /// Subscription id on the server. Used to look up and
        /// locate the subscription from messages received.
        /// </summary>
        uint Id { get; }

        /// <summary>
        /// Current message generation. Recreating, deleting or resetting the
        /// server-side subscription advances it, so acknowledgements stamped
        /// with an older generation are known to be stale.
        /// </summary>
        long MessageGeneration { get; }

        /// <summary>
        /// Gets the acknowledgement scope of a message generation of this
        /// subscription. The scope of the current generation is cached, so
        /// handles created while dispatching a message and the
        /// acknowledgement queued for it compare equal.
        /// </summary>
        /// <param name="generation">The message generation.</param>
        AcknowledgementScope GetAcknowledgementScope(long generation);

        /// <summary>
        /// Allows the session to add the notification message
        /// to the subscription for dispatch. This is called by
        /// the subscription manager when a message is received.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="availableSequenceNumbers"></param>
        /// <param name="stringTable"></param>
        /// <param name="waitPolicy">Decides whether the caller may wait for room
        /// when the ingress queue is full. <c>null</c> always waits.</param>
        /// <returns><c>false</c> when the queue was full and the policy did not
        /// allow the caller to wait: the message was not admitted, and it must
        /// not be acknowledged, so the server keeps it for Republish.</returns>
        ValueTask<bool> OnPublishReceivedAsync(NotificationMessage message,
            IReadOnlyList<uint>? availableSequenceNumbers,
            IReadOnlyList<string> stringTable,
            IIngressWaitPolicy? waitPolicy = null);
    }

    /// <summary>
    /// Decides whether a publish worker may park on the full ingress queue
    /// of one subscription. Parked workers send no Publish requests, so a
    /// policy keeps enough workers free for the other subscriptions.
    /// </summary>
    internal interface IIngressWaitPolicy
    {
        /// <summary>
        /// Called before the caller waits for room. Returns <c>false</c> when
        /// it must not wait.
        /// </summary>
        bool TryEnterWait();

        /// <summary>
        /// Called when a wait admitted by <see cref="TryEnterWait"/> ends.
        /// </summary>
        void ExitWait();
    }
}
