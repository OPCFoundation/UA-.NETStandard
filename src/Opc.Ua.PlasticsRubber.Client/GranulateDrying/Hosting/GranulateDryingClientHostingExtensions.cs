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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.GranulateDrying.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers drying clients over the existing managed-session factory.
    /// </summary>
    public static class GranulateDryingClientHostingExtensions
    {
        /// <summary>
        /// Adds a typed client factory without creating or owning a second session transport.
        /// </summary>
        public static IOpcUaClientBuilder AddGranulateDryingClient(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<GranulateDryingClientFactory>(services =>
                new GranulateDryingClientFactory(
                    services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>(),
                    services.GetRequiredService<ITelemetryContext>()));
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<GranulateDryingClient>>>(services =>
                services.GetRequiredService<GranulateDryingClientFactory>().CreateAsync);
            return builder;
        }
    }
}

namespace Opc.Ua.GranulateDrying.Client
{
    /// <summary>
    /// Acquires the existing ManagedSession and opens the complete typed drying interface.
    /// </summary>
    public sealed class GranulateDryingClientFactory
    {
        /// <summary>
        /// Creates an injectable or directly constructed session-bound factory.
        /// </summary>
        public GranulateDryingClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessionFactory,
            ITelemetryContext telemetry)
        {
            m_sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Returns a fresh facade over the managed session without transferring session ownership.
        /// </summary>
        public async Task<GranulateDryingClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessionFactory(cancellationToken).ConfigureAwait(false);
            return new GranulateDryingClient(session, m_telemetry);
        }

        /// <summary>
        /// The existing session factory.
        /// </summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessionFactory;

        /// <summary>
        /// The telemetry passed to generated proxies.
        /// </summary>
        private readonly ITelemetryContext m_telemetry;
    }
}
