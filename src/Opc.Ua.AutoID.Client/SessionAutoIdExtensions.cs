/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Client;

namespace Opc.Ua.AutoID.Client
{
    /// <summary>
    /// Convenience extensions for opening an AutoID client from a session.
    /// </summary>
    public static class SessionAutoIdExtensions
    {
        /// <summary>
        /// Creates an AutoID client over the supplied session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static AutoIdClient AutoId(this ISession session, ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return new AutoIdClient(session, telemetry);
        }
    }
}
