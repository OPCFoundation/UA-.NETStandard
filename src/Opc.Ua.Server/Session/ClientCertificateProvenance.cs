// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Records, for any <see cref="ISession"/> implementation, whether its client
    /// application certificate passed the server's validation when the session was
    /// created.
    /// </summary>
    /// <remarks>
    /// A certificate whose validation error an
    /// <see cref="StandardServer.OnApplicationCertificateError"/> override accepted
    /// still signs the session, but it does not establish a trusted application
    /// identity for role assignment (OPC 10000-3 4.9, OPC 10000-18 4.4.4). The
    /// provenance is kept outside the session object so that a session created by a
    /// custom <see cref="SessionManager"/> factory is covered like a
    /// <see cref="Session"/>.
    /// </remarks>
    internal static class ClientCertificateProvenance
    {
        /// <summary>
        /// Whether the client certificate of the session passed validation. A
        /// session that was never marked is validated.
        /// </summary>
        public static bool IsValidated(ISession session)
        {
            return !s_unvalidated.TryGetValue(session, out _);
        }

        /// <summary>
        /// Records whether the client certificate of the session passed validation.
        /// </summary>
        public static void SetValidated(ISession session, bool validated)
        {
            if (validated)
            {
                s_unvalidated.Remove(session);
            }
            else
            {
                s_unvalidated.GetValue(session, static _ => s_marker);
            }
        }

        private static readonly object s_marker = new();

        private static readonly ConditionalWeakTable<ISession, object> s_unvalidated =
#if NET8_0_OR_GREATER
            [];
#else
            new();
#endif
    }
}
