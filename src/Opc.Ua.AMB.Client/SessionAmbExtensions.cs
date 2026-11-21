// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Client;

namespace Opc.Ua.AMB.Client
{
    /// <summary>
    /// Opens the Asset Management Basics client on a session.
    /// </summary>
    public static class SessionAmbExtensions
    {
        /// <summary>
        /// Creates an <see cref="AmbClient"/> for the session.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <returns>The client.</returns>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public static AmbClient AssetManagement(this ISession session, ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return new AmbClient(session, telemetry);
        }
    }
}
