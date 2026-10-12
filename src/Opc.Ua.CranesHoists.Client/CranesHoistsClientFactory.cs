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

namespace Opc.Ua.CranesHoists.Client
{
    /// <summary>
    /// Creates typed crane clients over the application's managed-session factory.
    /// </summary>
    public sealed class CranesHoistsClientFactory
    {
        /// <summary>
        /// Captures injected session acquisition and telemetry.
        /// </summary>
        public CranesHoistsClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry,
            CranesHoistsClientOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_options = options ?? new CranesHoistsClientOptions();
            m_options.Validate();
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Acquires a session without taking ownership of that session.
        /// </summary>
        public async Task<CranesHoistsClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new CranesHoistsClient(session, m_telemetry, m_options, m_timeProvider);
        }

        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;
        private readonly ITelemetryContext m_telemetry;
        private readonly CranesHoistsClientOptions m_options;
        private readonly TimeProvider m_timeProvider;
    }

    /// <summary>
    /// Creates typed crane clients without a server-package dependency.
    /// </summary>
    public static class SessionCranesHoistsExtensions
    {
        /// <summary>
        /// Extends an existing session with bounded crane workflows.
        /// </summary>
        public static CranesHoistsClient CranesHoists(
            this ISession session,
            ITelemetryContext telemetry,
            CranesHoistsClientOptions? options = null)
        {
            return new CranesHoistsClient(session, telemetry, options);
        }
    }
}
