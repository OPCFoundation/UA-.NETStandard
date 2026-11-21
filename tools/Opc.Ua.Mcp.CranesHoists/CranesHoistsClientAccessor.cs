// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.CranesHoists.Client;

[assembly: CLSCompliant(false)]

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Opens bounded crane clients over borrowed, named MCP sessions.
    /// </summary>
    public sealed class CranesHoistsClientAccessor
    {
        /// <summary>
        /// Captures shared session access, optional discovery bounds and the observation clock.
        /// </summary>
        public CranesHoistsClientAccessor(
            OpcUaSessionManager sessionManager,
            CranesHoistsClientOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            m_options = options ?? new CranesHoistsClientOptions();
            m_options.Validate();
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Opens the typed client without acquiring ownership of the session or its existing subscriptions.
        /// </summary>
        public CranesHoistsClient CreateClient(string? sessionName = null)
        {
            return new CranesHoistsClient(m_sessionManager.GetSessionOrThrow(sessionName),
                m_sessionManager.Telemetry, m_options, m_timeProvider);
        }

        /// <summary>The shared session manager owned by the embedding host.</summary>
        private readonly OpcUaSessionManager m_sessionManager;

        /// <summary>The validated typed-client browse and inventory limits.</summary>
        private readonly CranesHoistsClientOptions m_options;

        /// <summary>The observation clock owned by the embedding host.</summary>
        private readonly TimeProvider m_timeProvider;
    }
}
