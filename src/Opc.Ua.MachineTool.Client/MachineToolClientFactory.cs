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

namespace Opc.Ua.MachineTool.Client
{
    /// <summary>
    /// Creates clients that borrow application- or container-owned managed sessions.
    /// </summary>
    public sealed class MachineToolClientFactory
    {
        /// <summary>
        /// Creates an injectable or directly constructed client factory.
        /// </summary>
        /// <param name="sessionFactory">Acquires a managed session whose ownership remains with its provider.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="timeProvider">The clock used for bounded observations.</param>
        public MachineToolClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Acquires a session without taking ownership; asynchronously dispose the client to drain its observations.
        /// </summary>
        /// <param name="cancellationToken">Cancels connection setup.</param>
        /// <returns>A client that borrows the acquired session.</returns>
        public async Task<MachineToolClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new MachineToolClient(session, m_telemetry, m_timeProvider);
        }

        /// <summary>The application's managed-session accessor.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;

        /// <summary>The telemetry context.</summary>
        private readonly ITelemetryContext m_telemetry;

        /// <summary>The observation clock.</summary>
        private readonly TimeProvider m_timeProvider;
    }
}
