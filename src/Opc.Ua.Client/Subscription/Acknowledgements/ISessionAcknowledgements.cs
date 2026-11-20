/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Client
{
    /// <summary>
    /// The acknowledgement facet of a session.
    /// </summary>
    public interface ISessionAcknowledgements
    {
        /// <summary>
        /// Gets when the session acknowledges notifications.
        /// </summary>
        AcknowledgementMode Mode { get; }

        /// <summary>
        /// Confirms that a notification was processed, so that its acknowledgement can be
        /// sent. Only has an effect in <see cref="AcknowledgementMode.AfterConfirmation"/>
        /// mode, for an acknowledgement that has not ended yet. Confirming a handle more than
        /// once, after its acknowledgement ended, or after its subscription was retired has
        /// no effect.
        /// </summary>
        /// <param name="handle">The acknowledgement of the processed notification.</param>
        void Confirm(AcknowledgementHandle handle);
    }

    /// <summary>
    /// Extensions to obtain the acknowledgement facet of a session.
    /// </summary>
    public static class SessionAcknowledgementsExtensions
    {
        /// <summary>
        /// Gets the acknowledgement facet of a session, if it supports one.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="acknowledgements">The facet.</param>
        /// <returns><c>true</c> if the session supports the facet.</returns>
        public static bool TryGetAcknowledgements(
            this ISession session,
            [NotNullWhen(true)] out ISessionAcknowledgements? acknowledgements)
        {
            acknowledgements = session as ISessionAcknowledgements;
            return acknowledgements != null;
        }
    }
}
