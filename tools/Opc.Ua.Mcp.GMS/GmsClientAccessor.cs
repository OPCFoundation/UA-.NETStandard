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
using Opc.Ua.GMS.Client;

[assembly: CLSCompliant(false)]

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Opens call-owned GMS facades over borrowed named MCP sessions with host-enforced read bounds.
    /// </summary>
    public sealed class GmsClientAccessor
    {
        /// <summary>
        /// Captures the shared session manager, native read limits and observation clock.
        /// </summary>
        public GmsClientAccessor(
            OpcUaSessionManager sessionManager, GmsClientOptions? options = null, TimeProvider? timeProvider = null)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            m_options = options ?? new GmsClientOptions();
            m_options.Validate();
            TimeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Gets the host's clock for bounded intermediate-result observations.
        /// </summary>
        internal TimeProvider TimeProvider { get; }

        /// <summary>
        /// Opens a facade which must be disposed after use, without owning the session or unrelated subscriptions.
        /// Caller limits can narrow but never increase the embedding host's native read limits.
        /// </summary>
        public GmsClient CreateClient(
            string? sessionName = null, int maximumElements = 100, int maximumPayloadBytes = 131072)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumElements, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumElements, 100);
            Tools.GmsParameters.ResponseLimit(maximumPayloadBytes);
            if (sessionName != null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(sessionName.Length, 128);
            }
            return new GmsClient(m_sessionManager.GetSessionOrThrow(sessionName), m_sessionManager.Telemetry,
                m_options with
                {
                    MaximumElements = Math.Min(m_options.MaximumElements, maximumElements),
                    MaximumPayloadBytes = Math.Min(m_options.MaximumPayloadBytes, maximumPayloadBytes)
                }, TimeProvider);
        }

        /// <summary>The shared session manager owned by the embedding host.</summary>
        private readonly OpcUaSessionManager m_sessionManager;

        /// <summary>The embedding host's immutable upper bounds.</summary>
        private readonly GmsClientOptions m_options;
    }
}
