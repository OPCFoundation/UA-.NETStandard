// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Opc.Ua
{
    /// <summary>
    /// Canonical <see cref="ITelemetryContext"/> adapter used by
    /// <see cref="OpcUaServiceCollectionExtensions.AddOpcUa(IServiceCollection)"/>.
    /// Resolves the host's <see cref="ILoggerFactory"/> from DI at first
    /// access; falls back to <see cref="NullLoggerFactory"/> when no logger
    /// factory is registered. Activity sources and meters are derived from
    /// the calling assembly through the base implementation.
    /// </summary>
    public sealed class ServiceProviderTelemetryContext : TelemetryContextBase
    {
        /// <summary>
        /// Creates a new adapter bound to the supplied service provider.
        /// </summary>
        /// <param name="serviceProvider">The host's service provider.</param>
        /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> is <c>null</c>.</exception>
        public ServiceProviderTelemetryContext(IServiceProvider serviceProvider)
            : base(serviceProvider is null
                ? throw new ArgumentNullException(nameof(serviceProvider))
                : serviceProvider.GetService<ILoggerFactory>()
                    ?? NullLoggerFactory.Instance)
        {
        }
    }
}
