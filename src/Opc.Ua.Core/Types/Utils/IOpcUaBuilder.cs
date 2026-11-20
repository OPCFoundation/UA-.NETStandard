// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// Root builder returned by
    /// <see cref="Microsoft.Extensions.DependencyInjection.OpcUaServiceCollectionExtensions.AddOpcUa(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
    /// Feature libraries hang their own <c>.AddXxx(...)</c> extension methods
    /// off this interface so the OPC UA DI surface is fluent and uniform.
    /// </summary>
    /// <remarks>
    /// Derives from the original <see cref="IDependencyInjectionBuilder"/> to
    /// preserve binary compatibility for any existing
    /// <c>AddLogging(this IDependencyInjectionBuilder)</c> /
    /// <c>AddMetrics(this IDependencyInjectionBuilder)</c> callers. Newer
    /// fluent overloads accept and return <see cref="IOpcUaBuilder"/> so
    /// chaining into feature methods works.
    /// </remarks>
    public interface IOpcUaBuilder : IOpcUaTransportBuilder;
}
