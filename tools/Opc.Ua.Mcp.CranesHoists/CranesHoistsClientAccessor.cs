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
