// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Client.Network
{
    /// <summary>
    /// Creates typed Part 22 clients over caller-owned sessions.
    /// </summary>
    public sealed class NetworkModelClientFactory
    {
        /// <summary>
        /// Creates a factory with the application's telemetry context.
        /// </summary>
        public NetworkModelClientFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a session-bound Part 22 client without taking session ownership.
        /// </summary>
        public NetworkModelClient Create(ISessionClient session)
        {
            return new NetworkModelClient(session, m_telemetry);
        }

        /// <summary>Application-owned telemetry for each session-bound client.</summary>
        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Direct construction helper for a session-bound Part 22 client.
    /// </summary>
    public static class NetworkModelClientExtensions
    {
        /// <summary>
        /// Creates a Part 22 client over the caller-owned session.
        /// </summary>
        public static NetworkModelClient GetNetworkModelClient(
            this ISessionClient session,
            ITelemetryContext telemetry)
        {
            return new NetworkModelClient(session, telemetry);
        }
    }
}
