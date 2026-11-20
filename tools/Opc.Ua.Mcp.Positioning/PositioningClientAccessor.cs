/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Positioning.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Creates RSL and GPOS clients over the currently selected named session.
    /// </summary>
    public sealed class PositioningClientAccessor
    {
        /// <summary>
        /// Initializes the accessor without caching a session or proxy.
        /// </summary>
        public PositioningClientAccessor(OpcUaSessionManager sessionManager)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Creates a relative spatial location client for this call.
        /// </summary>
        public RelativeSpatialLocationClient CreateRelativeClient(string? sessionName = null)
        {
            return new RelativeSpatialLocationClient(
                m_sessionManager.GetSessionOrThrow(sessionName),
                m_sessionManager.Telemetry);
        }

        /// <summary>
        /// Creates a global positioning client for this call.
        /// </summary>
        public GlobalPositioningClient CreateGlobalClient(string? sessionName = null)
        {
            return new GlobalPositioningClient(
                m_sessionManager.GetSessionOrThrow(sessionName),
                m_sessionManager.Telemetry);
        }

        /// <summary>
        /// The owner of the currently connected sessions.
        /// </summary>
        private readonly OpcUaSessionManager m_sessionManager;
    }
}
