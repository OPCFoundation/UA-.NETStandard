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
