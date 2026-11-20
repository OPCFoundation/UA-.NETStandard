/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Classifies a failed call of the optional Server.GetMonitoredItems
    /// method (OPC 10000-5, 9.1) that a client uses to map the monitored
    /// items of a transferred subscription.
    /// </summary>
    internal static class GetMonitoredItemsFallback
    {
        /// <summary>
        /// Whether the failed call means the server does not provide a
        /// usable GetMonitoredItems method, so the client may map the items
        /// another way. Stacks without the method answer BadMethodInvalid,
        /// BadNotSupported, BadNotImplemented, BadNothingToDo (asyncua) or
        /// BadInternalError (open62541 without XML encoding), and the set is
        /// open ended, so every error counts except the ones that prove
        /// something else: a transient error (timeout, busy server, lost
        /// connection or session) after which a retry can still succeed,
        /// and a rejected subscription, which no mapping can bring back.
        /// </summary>
        public static bool IsMethodUnavailable(StatusCode status)
        {
            return StatusCode.IsBad(status) &&
                !IsTransientFailure(status) &&
                !s_subscriptionRejected.Contains(status.CodeBits);
        }

        /// <summary>
        /// Whether the call failed with a transient error after which a
        /// retry can still succeed (timeout, busy server, lost connection or
        /// session).
        /// </summary>
        public static bool IsTransientFailure(StatusCode status)
        {
            return s_transientFailures.Contains(status.CodeBits);
        }

        /// <summary>
        /// The method exists but the subscription is gone or not ours.
        /// </summary>
        private static readonly HashSet<uint> s_subscriptionRejected =
        [
            StatusCodes.BadSubscriptionIdInvalid.CodeBits,
            StatusCodes.BadNoSubscription.CodeBits
        ];

        private static readonly HashSet<uint> s_transientFailures =
        [
            StatusCodes.BadTimeout.CodeBits,
            StatusCodes.BadRequestTimeout.CodeBits,
            StatusCodes.BadTooManyOperations.CodeBits,
            StatusCodes.BadServerTooBusy.CodeBits,
            StatusCodes.BadResourceUnavailable.CodeBits,
            StatusCodes.BadOutOfMemory.CodeBits,
            StatusCodes.BadCommunicationError.CodeBits,
            StatusCodes.BadConnectionClosed.CodeBits,
            StatusCodes.BadNotConnected.CodeBits,
            StatusCodes.BadServerNotConnected.CodeBits,
            StatusCodes.BadNoCommunication.CodeBits,
            StatusCodes.BadDisconnect.CodeBits,
            StatusCodes.BadSecureChannelClosed.CodeBits,
            StatusCodes.BadSecureChannelIdInvalid.CodeBits,
            StatusCodes.BadSessionClosed.CodeBits,
            StatusCodes.BadSessionIdInvalid.CodeBits,
            StatusCodes.BadSessionNotActivated.CodeBits,
            StatusCodes.BadRequestInterrupted.CodeBits,
            StatusCodes.BadRequestCancelledByClient.CodeBits,
            StatusCodes.BadOperationAbandoned.CodeBits,
            StatusCodes.BadShutdown.CodeBits,
            StatusCodes.BadServerHalted.CodeBits
        ];
    }
}
