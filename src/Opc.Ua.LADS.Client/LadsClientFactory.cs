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

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// Creates <see cref="LadsClient"/> instances on sessions a hosted client
    /// opens.
    /// </summary>
    public sealed class LadsClientFactory
    {
        /// <summary>
        /// Creates the factory with the default options.
        /// </summary>
        /// <param name="sessionFactory">Opens a session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public LadsClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
            : this(sessionFactory, telemetry, null)
        {
        }

        /// <summary>
        /// Creates the factory with options.
        /// </summary>
        /// <param name="sessionFactory">Opens a session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="options">The options; <c>null</c> uses <see cref="LadsClientOptions.Default"/>.</param>
        /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
        public LadsClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry,
            LadsClientOptions? options)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            options?.Validate();
            m_options = options;
        }

        /// <summary>
        /// Opens a session and creates a client on it.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The client.</returns>
        public async Task<LadsClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new LadsClient(session, m_telemetry, m_options);
        }

        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;
        private readonly ITelemetryContext m_telemetry;
        private readonly LadsClientOptions? m_options;
    }
}
