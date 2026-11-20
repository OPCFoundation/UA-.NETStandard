// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Client.Programs
{
    /// <summary>
    /// Injectable factory for clients bound to caller-owned sessions.
    /// </summary>
    public sealed class ProgramClientFactory
    {
        /// <summary>
        /// Creates a factory using the application's telemetry context.
        /// </summary>
        public ProgramClientFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a fresh client without taking ownership of the session.
        /// </summary>
        public ProgramClient Create(ISessionClient session)
        {
            return new ProgramClient(session, m_telemetry);
        }

        /// <summary>
        /// The application's telemetry context.
        /// </summary>
        private readonly ITelemetryContext m_telemetry;
    }
}
