// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.AMB.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Creates an AMB client on the current named session for each tool invocation.
    /// </summary>
    public sealed class AmbClientAccessor
    {
        /// <summary>
        /// Initializes the accessor.
        /// </summary>
        public AmbClientAccessor(OpcUaSessionManager sessionManager)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Creates a client without retaining a session or proxy between calls.
        /// </summary>
        public AmbClient CreateClient(string? sessionName = null, bool requireSupported = true)
        {
            var client = new AmbClient(m_sessionManager.GetSessionOrThrow(sessionName), m_sessionManager.Telemetry);
            if (requireSupported && !client.IsSupported)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The selected session does not publish the AMB namespace.");
            }
            return client;
        }

        /// <summary>
        /// The session registry owned by the embedding host.
        /// </summary>
        private readonly OpcUaSessionManager m_sessionManager;
    }
}
