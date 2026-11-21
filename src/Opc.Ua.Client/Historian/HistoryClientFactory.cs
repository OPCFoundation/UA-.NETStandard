// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Client.Historian
{
    /// <summary>
    /// Dependency-injected factory for per-session history clients.
    /// </summary>
    public sealed class HistoryClientFactory
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public HistoryClientFactory(ITelemetryContext telemetry)
        {
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a new <see cref="HistoryClient"/> bound to
        /// <paramref name="session"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public HistoryClient Create(ISession session, HistoryClientOptions? options = null)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            return new HistoryClient(session, options);
        }

        /// <summary>
        /// The shared telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }
    }
}
