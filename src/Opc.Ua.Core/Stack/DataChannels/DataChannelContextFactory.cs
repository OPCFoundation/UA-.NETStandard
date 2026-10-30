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
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Directly constructible factory sharing admission, clocks and policy across authenticated carriers.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelContextFactory : IDataChannelContextFactory
    {
        /// <summary>
        /// Creates a factory without starting transports or requiring a Server or Session.
        /// </summary>
        public DataChannelContextFactory(
            IDataChannelAuthorizer authorizer,
            DataChannelOptions options,
            DataChannelApplicationBudget budget,
            TimeProvider timeProvider,
            ITelemetryContext telemetry)
        {
            m_authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_budget = budget ?? throw new ArgumentNullException(nameof(budget));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <inheritdoc/>
        public DataChannelContext Create(IDataChannelCarrier carrier)
        {
            return new DataChannelContext(carrier, m_authorizer, m_options, m_budget, m_timeProvider, m_telemetry);
        }

        private readonly IDataChannelAuthorizer m_authorizer;
        private readonly DataChannelOptions m_options;
        private readonly DataChannelApplicationBudget m_budget;
        private readonly TimeProvider m_timeProvider;
        private readonly ITelemetryContext m_telemetry;
    }

    /// <summary>
    /// Registers common flow services without installing or advertising a carrier implementation.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public static class DataChannelBuilderExtensions
    {
        /// <summary>
        /// Adds the injectable common runtime. Register an explicit IDataChannelAuthorizer;
        /// absence of resource policy must never grant access by default.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaBuilder AddDataChannels(
            this IOpcUaBuilder builder,
            DataChannelOptions? options = null,
            DataChannelApplicationLimits? applicationLimits = null)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.TryAddSingleton(options ?? new DataChannelOptions());
            builder.Services.TryAddSingleton(applicationLimits ?? new DataChannelApplicationLimits());
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.TryAddSingleton<DataChannelApplicationBudget>();
            builder.Services.TryAddSingleton<DataChannelGrantStore>();
            builder.Services.TryAddSingleton<IDataChannelContextFactory, DataChannelContextFactory>();
            return builder;
        }
    }
}
