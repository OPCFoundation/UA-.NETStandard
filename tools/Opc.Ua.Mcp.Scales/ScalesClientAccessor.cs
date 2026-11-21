// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Scales.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Creates per-call scale clients over the currently selected named session.
    /// </summary>
    public sealed class ScalesClientAccessor
    {
        /// <summary>
        /// Initializes the accessor without retaining a session or proxy.
        /// </summary>
        public ScalesClientAccessor(OpcUaSessionManager sessionManager)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Creates a client using the named session, or the sole active session.
        /// </summary>
        public ScalesClient CreateClient(string? sessionName = null)
        {
            var client = new ScalesClient(
                m_sessionManager.GetSessionOrThrow(sessionName),
                m_sessionManager.Telemetry);
            if (!client.IsSupported)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The selected session does not publish the Scales namespace.");
            }
            return client;
        }

        private readonly OpcUaSessionManager m_sessionManager;
    }
}
