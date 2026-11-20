/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Client;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Convenience extensions for opening a high-level Vision client from an
    /// existing session, without DI.
    /// </summary>
    public static class SessionVisionExtensions
    {
        /// <summary>
        /// Creates a high-level Vision client over the supplied session.
        /// </summary>
        /// <param name="session">
        /// The connected session.
        /// </param>
        /// <param name="telemetry">
        /// The telemetry context used by generated proxies.
        /// </param>
        /// <exception cref="ArgumentNullException"></exception>
        public static VisionClient Vision(
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
            return new VisionClient(session, telemetry);
        }
    }
}
