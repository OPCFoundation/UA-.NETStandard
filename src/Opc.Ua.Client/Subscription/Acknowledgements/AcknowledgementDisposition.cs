/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client
{
    /// <summary>
    /// How an acknowledgement ended. Every acknowledgement reported to an
    /// <see cref="ISubscriptionAcknowledgementObserver"/> ends exactly once.
    /// </summary>
    public enum AcknowledgementDisposition
    {
        /// <summary>
        /// Not used.
        /// </summary>
        None = 0,

        /// <summary>
        /// The server confirmed the acknowledgement.
        /// </summary>
        Acknowledged = 1,

        /// <summary>
        /// The server no longer holds the message or rejected the acknowledgement, which is
        /// then not sent again. The status tells why.
        /// </summary>
        NotRetained = 2,

        /// <summary>
        /// The subscription that produced the message was deleted, recreated or completed,
        /// another subscription now owns its id, or the session ended.
        /// </summary>
        OwnerRetired = 3,

        /// <summary>
        /// The acknowledgement could not be sent or kept, for example because the
        /// subscription manager was disposed while it was pending or in flight, or the
        /// obsolete deferral event dropped it.
        /// </summary>
        Abandoned = 4
    }
}
