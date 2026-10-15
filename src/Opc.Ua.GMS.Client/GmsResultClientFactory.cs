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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;

namespace Opc.Ua.GMS.Client
{
    /// <summary>
    /// Injectable factory for instance-bound result workflows over an externally owned session.
    /// </summary>
    public interface IGmsResultClientFactory
    {
        /// <summary>
        /// Resolves a session and creates a result workflow for the specified canonical GMS instance.
        /// </summary>
        ValueTask<GmsResultClient> CreateAsync(NodeId gmsNodeId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Uses an injected session accessor without constructing another connection or assuming session ownership.
    /// </summary>
    public sealed class GmsResultClientFactory : IGmsResultClientFactory
    {
        /// <summary>
        /// Captures the session accessor, telemetry and immutable result limits.
        /// </summary>
        public GmsResultClientFactory(
            Func<CancellationToken, ValueTask<ISession>> sessionAccessor,
            ITelemetryContext telemetry,
            GmsResultLimits? limits = null)
        {
            m_sessionAccessor = sessionAccessor ?? throw new ArgumentNullException(nameof(sessionAccessor));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_limits = limits ?? new GmsResultLimits();
            m_limits.Validate();
        }

        /// <inheritdoc/>
        public async ValueTask<GmsResultClient> CreateAsync(
            NodeId gmsNodeId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (gmsNodeId.IsNull)
            {
                throw new ArgumentException("A GMS instance is required.", nameof(gmsNodeId));
            }
            ISession session = await m_sessionAccessor(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new GmsResultClient(session, gmsNodeId, m_telemetry, m_limits);
        }

        private readonly Func<CancellationToken, ValueTask<ISession>> m_sessionAccessor;
        private readonly ITelemetryContext m_telemetry;
        private readonly GmsResultLimits m_limits;
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Composes GMS results with the existing injectable managed-session infrastructure.
    /// </summary>
    public static class OpcUaGmsResultClientExtensions
    {
        /// <summary>
        /// Registers an instance-bound result-client factory without adding a server runtime or owning a session.
        /// </summary>
        public static IOpcUaClientBuilder AddGmsResultsClient(
            this IOpcUaClientBuilder builder,
            GmsResultLimits? limits = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            GmsResultLimits selected = limits ?? new GmsResultLimits();
            selected.Validate();
            builder.Services.TryAddSingleton<IGmsResultClientFactory>(services => new GmsResultClientFactory(
                async cancellationToken =>
                    await services.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>()(
                        cancellationToken).ConfigureAwait(false),
                services.GetRequiredService<ITelemetryContext>(),
                selected));
            return builder;
        }
    }
}
