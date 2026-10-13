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
