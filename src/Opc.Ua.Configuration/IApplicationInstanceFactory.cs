/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Creates fresh <see cref="IApplicationInstance"/> objects for a given
    /// hosted OPC UA server. Registered as a singleton by
    /// <see cref="Microsoft.Extensions.DependencyInjection.OpcUaConfigurationServiceCollectionExtensions.AddApplicationInstance(IOpcUaBuilder)"/>.
    /// </summary>
    /// <remarks>
    /// Each hosted server requires its own
    /// <see cref="IApplicationInstance"/> with distinct PKI roots,
    /// endpoint URLs, and certificate stores. A process-wide singleton
    /// would cause configuration overwrite and lifecycle conflicts;
    /// this factory keeps a single registered service that produces
    /// per-host instances on demand.
    /// </remarks>
    public interface IApplicationInstanceFactory
    {
        /// <summary>
        /// Creates a new <see cref="IApplicationInstance"/> using the
        /// supplied <paramref name="telemetry"/> for logging / tracing.
        /// </summary>
        /// <param name="telemetry">The telemetry context the new
        /// application instance should use.</param>
        IApplicationInstance Create(ITelemetryContext telemetry);
    }
}
