/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
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
