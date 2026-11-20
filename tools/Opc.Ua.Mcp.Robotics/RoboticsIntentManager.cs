/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Robotics.Client.Intent;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Creates Robot Intent clients from active MCP OPC UA sessions.
    /// </summary>
    public sealed class RoboticsIntentManager
    {
        /// <summary>
        /// Initializes the manager.
        /// </summary>
        public RoboticsIntentManager(OpcUaSessionManager sessionManager)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Creates a discovery client over the named or sole active session.
        /// </summary>
        public RobotIntentClient CreateClient(string? sessionName = null)
        {
            ISession session = m_sessionManager.GetSessionOrThrow(sessionName);
            return new RobotIntentClient(session, m_sessionManager.Telemetry);
        }

        /// <summary>
        /// Creates a controller client over the named or sole active session.
        /// Accepts a NodeId string directly; does not resolve names.
        /// </summary>
        public RobotIntentControllerClient OpenController(string controllerId, string? sessionName = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(controllerId);

            return CreateClient(sessionName).Controller(Serialization.OpcUaJsonHelper.ParseNodeId(controllerId));
        }

        /// <summary>
        /// Resolves a controller selector (unique name, BrowseName, or NodeId string) to a
        /// controller client. The selector is trimmed and matched with exact ordinal comparison.
        /// Exactly one discovery client is created per call.
        /// </summary>
        public ValueTask<RobotIntentControllerClient> ResolveControllerAsync(
            string controller,
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return RoboticsControllerResolver.ResolveAsync(CreateClient(sessionName), controller, ct);
        }

        private readonly OpcUaSessionManager m_sessionManager;
    }
}
