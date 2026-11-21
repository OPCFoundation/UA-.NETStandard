// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Client;

namespace Opc.Ua.Machinery.Client
{
    /// <summary>
    /// Convenience extensions for opening a Machinery client from a session.
    /// </summary>
    public static class SessionMachineryExtensions
    {
        /// <summary>
        /// Creates a Machinery client over the supplied session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context used by generated proxies.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static MachineryClient Machinery(
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
            return new MachineryClient(session, telemetry);
        }

        /// <summary>
        /// Creates a Machinery client with bounded discovery and strict, evidenced reads.
        /// </summary>
        /// <param name="session">The borrowed, connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="options">The positive read and browse limits.</param>
        public static MachineryClient Machinery(
            this ISession session,
            ITelemetryContext telemetry,
            MachineryClientOptions options)
        {
            return new MachineryClient(session, telemetry, options);
        }
    }
}
