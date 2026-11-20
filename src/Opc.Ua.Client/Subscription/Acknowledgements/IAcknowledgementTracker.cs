/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client
{
    /// <summary>
    /// Tracks acknowledgements from the time their notification is delivered until their
    /// terminal outcome, so that confirmations can be matched to live acknowledgements only.
    /// </summary>
    internal interface IAcknowledgementTracker
    {
        /// <summary>
        /// Starts tracking an acknowledgement. Called before the notification is delivered;
        /// tracking an acknowledgement that is already tracked has no effect.
        /// </summary>
        /// <param name="handle">The acknowledgement of the notification.</param>
        void Track(AcknowledgementHandle handle);

        /// <summary>
        /// Retires internal confirmation state before an application observer is notified.
        /// </summary>
        /// <param name="handle">The acknowledgement that ended.</param>
        void Complete(AcknowledgementHandle handle);

        /// <summary>
        /// Stops tracking an acknowledgement that moves to another session, without an outcome.
        /// </summary>
        /// <param name="handle">The acknowledgement that moves.</param>
        /// <returns><c>true</c> if the application had confirmed the notification.</returns>
        bool Release(AcknowledgementHandle handle);
    }
}
