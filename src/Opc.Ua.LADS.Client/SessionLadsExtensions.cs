// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Client;

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// Extensions that create a <see cref="LadsClient"/> on a connected session.
    /// </summary>
    public static class SessionLadsExtensions
    {
        /// <summary>
        /// Creates a LADS client that borrows the session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <returns>The client.</returns>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public static LadsClient GetLadsClient(this ISession session, ITelemetryContext telemetry)
        {
            return new LadsClient(session, telemetry);
        }

        /// <summary>
        /// Creates a LADS client with options that borrows the session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="options">The options; <c>null</c> uses <see cref="LadsClientOptions.Default"/>.</param>
        /// <returns>The client.</returns>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
        public static LadsClient GetLadsClient(
            this ISession session,
            ITelemetryContext telemetry,
            LadsClientOptions? options)
        {
            return new LadsClient(session, telemetry, options);
        }
    }
}
