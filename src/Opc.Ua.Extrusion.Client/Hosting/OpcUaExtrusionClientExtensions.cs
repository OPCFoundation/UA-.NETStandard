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
using Opc.Ua.Extrusion.Client;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers extrusion clients on the existing ManagedSession factory.
    /// </summary>
    public static class OpcUaExtrusionClientExtensions
    {
        /// <summary>
        /// Adds the typed extrusion facade without opening a second connection or changing identity.
        /// </summary>
        public static IOpcUaClientBuilder AddExtrusionClient(this IOpcUaClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton<ExtrusionClientFactory>();
            builder.Services.TryAddSingleton<Func<CancellationToken, Task<ExtrusionClient>>>(services =>
                services.GetRequiredService<ExtrusionClientFactory>().CreateAsync);
            return builder;
        }
    }
}

namespace Opc.Ua.Extrusion.Client
{
    /// <summary>
    /// Injectable factory backed by the existing managed-session provider.
    /// </summary>
    public sealed class ExtrusionClientFactory
    {
        /// <summary>
        /// Creates a typed client factory.
        /// </summary>
        public ExtrusionClientFactory(
            Func<CancellationToken, Task<ManagedSession>> sessions,
            ITelemetryContext telemetry)
        {
            m_sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Resolves the caller's managed session and creates the typed facade.
        /// </summary>
        public async Task<ExtrusionClient> CreateAsync(CancellationToken cancellationToken = default)
        {
            ManagedSession session = await m_sessions(cancellationToken).ConfigureAwait(false);
            return new ExtrusionClient(session, m_telemetry);
        }

        /// <summary>The caller-configured session acquisition function.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_sessions;

        /// <summary>The injected telemetry context.</summary>
        private readonly ITelemetryContext m_telemetry;
    }
}
