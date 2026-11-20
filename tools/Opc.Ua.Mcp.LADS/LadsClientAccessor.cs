// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Client;
using Opc.Ua.LADS.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Creates a per-call LADS client over the host-owned named session.
    /// </summary>
    public sealed class LadsClientAccessor
    {
        /// <summary>
        /// Creates the accessor.
        /// </summary>
        public LadsClientAccessor(OpcUaSessionManager sessions)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        /// <summary>
        /// Resolves the session and creates a typed client.
        /// </summary>
        public (LadsClient Client, ISession Session) CreateClient(string? sessionName)
        {
            ISession session = m_sessions.GetSessionOrThrow(sessionName);
            var client = new LadsClient(session, m_sessions.Telemetry);
            if (!client.IsSupported)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The selected session does not publish the LADS namespace.");
            }
            return (client, session);
        }

        private readonly OpcUaSessionManager m_sessions;
    }
}
