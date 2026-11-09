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
