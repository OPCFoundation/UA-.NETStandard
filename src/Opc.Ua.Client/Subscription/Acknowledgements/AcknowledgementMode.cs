// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Client
{
    /// <summary>
    /// When a session acknowledges the notifications it received.
    /// </summary>
    public enum AcknowledgementMode
    {
        /// <summary>
        /// Acknowledge a notification once it was dispatched to the application.
        /// </summary>
        Immediate = 0,

        /// <summary>
        /// Acknowledge a notification only after the application confirmed it with
        /// <see cref="ISessionAcknowledgements.Confirm"/>, for example once it was delivered
        /// downstream (at-least-once delivery). Until then the server keeps the message in
        /// its retransmission queue.
        /// </summary>
        AfterConfirmation = 1
    }
}
