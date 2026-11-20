/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.MachineTool.Client;

[assembly: CLSCompliant(false)]

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Opens typed MachineTool and WMTP facades over the host's borrowed, named sessions.
    /// </summary>
    public sealed class MachineToolClientAccessor
    {
        /// <summary>
        /// Captures the existing session manager and the host's observation clock.
        /// </summary>
        public MachineToolClientAccessor(OpcUaSessionManager sessionManager, TimeProvider? timeProvider = null)
        {
            m_sessions = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Creates a call-owned facade whose disposal drains observations but never disposes the borrowed session.
        /// </summary>
        public MachineToolClient CreateClient(string? sessionName = null)
        {
            return new MachineToolClient(
                m_sessions.GetSessionOrThrow(sessionName), m_sessions.Telemetry, m_timeProvider);
        }

        /// <summary>The embedding host's shared connection manager.</summary>
        private readonly OpcUaSessionManager m_sessions;

        /// <summary>The injected clock used by bounded observations.</summary>
        private readonly TimeProvider m_timeProvider;
    }
}
