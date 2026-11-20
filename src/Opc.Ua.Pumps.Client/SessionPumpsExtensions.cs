// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Client;

namespace Opc.Ua.Pumps.Client
{
    /// <summary>
    /// Convenience extensions for opening a Pumps client from a session.
    /// </summary>
    public static class SessionPumpsExtensions
    {
        /// <summary>
        /// Creates a Pumps client over the supplied session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        public static PumpsClient Pumps(
            this ISession session,
            ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return new PumpsClient(session, telemetry);
        }

        /// <summary>
        /// Creates a strictly validated client with a per-browse and per-value-set reference limit.
        /// </summary>
        /// <param name="session">The borrowed connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="maximumBrowseReferences">An inclusive limit from one through 65536.</param>
        public static PumpsClient Pumps(
            this ISession session,
            ITelemetryContext telemetry,
            int maximumBrowseReferences)
        {
            return new PumpsClient(session, telemetry, maximumBrowseReferences);
        }
    }
}
