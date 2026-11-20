// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Client;

namespace Opc.Ua.Robotics.Client
{
    /// <summary>
    /// Convenience extensions for opening high-level Robotics clients from a session.
    /// </summary>
    public static class SessionRoboticsExtensions
    {
        /// <summary>
        /// Creates a high-level Robotics client over the supplied session.
        /// </summary>
        /// <param name="session">
        /// The connected session.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used by generated proxies.
        /// </param>
        public static RoboticsClient Robotics(this ISession session, ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return new RoboticsClient(session, telemetry);
        }
    }
}
