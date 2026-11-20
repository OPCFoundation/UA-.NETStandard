/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client.Alarms
{
    /// <summary>
    /// Convenience extensions for obtaining an <see cref="AlarmClient"/>
    /// on a session.
    /// </summary>
    public static class AlarmClientSessionExtensions
    {
        /// <summary>
        /// Returns an <see cref="AlarmClient"/> bound to the supplied
        /// session. The supplied <paramref name="telemetry"/> is
        /// forwarded to the source-generated proxies the client
        /// constructs internally.
        /// </summary>
        /// <param name="session">The client session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        public static AlarmClient GetAlarmClient(
            this ISession session,
            ITelemetryContext telemetry)
        {
            return new AlarmClient(session, telemetry);
        }
    }
}
