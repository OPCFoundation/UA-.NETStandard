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

namespace Opc.Ua.PackML.Client
{
    /// <summary>
    /// Creates typed PackML clients over the application's managed-session factory.
    /// Session ownership remains with the application or its dependency-injection container.
    /// </summary>
    public sealed class PackMLClientFactory
    {
        /// <summary>
        /// Creates a factory over the existing session and telemetry services.
        /// </summary>
        public PackMLClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Borrows a managed session and creates a typed client; it never takes ownership of that session.
        /// </summary>
        public async Task<PackMLClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new PackMLClient(session, m_telemetry);
        }

        /// <summary>The application's session factory.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;

        /// <summary>The application's telemetry context.</summary>
        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Direct typed-client creation over an existing session.
    /// </summary>
    public static class SessionPackMLExtensions
    {
        /// <summary>
        /// Creates a PackML client borrowing this session.
        /// </summary>
        public static PackMLClient PackML(this ISession session, ITelemetryContext telemetry)
        {
            return new PackMLClient(session, telemetry);
        }
    }
}
