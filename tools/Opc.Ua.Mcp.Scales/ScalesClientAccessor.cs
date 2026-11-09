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
