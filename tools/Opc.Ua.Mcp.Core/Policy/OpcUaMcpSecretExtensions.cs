/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Explicit host integration for secret aliases and writable issued-credential storage.
    /// </summary>
    public static class OpcUaMcpSecretExtensions
    {
        /// <summary>
        /// Registers a shared fail-closed gateway for modules which accept secret references.
        /// Existing host registries and gateways are retained; no writable issuance store is selected.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpSecretReferenceDefaults(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddSingleton<ISecretRegistry, SecretRegistry>();
            services.TryAddSingleton(provider => new McpSecretReferences(
                provider.GetRequiredService<IMcpOperationPolicy>(),
                provider.GetRequiredService<ISecretRegistry>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System,
                telemetry: provider.GetService<ITelemetryContext>()));
            return services;
        }

        /// <summary>
        /// Registers a gateway over borrowed host stores. Omission of issuanceStore disables issuance;
        /// registering a registry never consents to an implicit in-memory writable store.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpSecrets(
            this IServiceCollection services,
            ISecretRegistry registry,
            ArrayOf<McpSecretAlias> aliases = default,
            ISecretStore? issuanceStore = null,
            string? issuanceStorePath = null,
            TimeProvider? timeProvider = null,
            int maxIssuedReferences = 128,
            int maxSecretBytes = 1_048_576)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(registry);
            services.TryAddSingleton(registry);
            services.TryAddSingleton(provider => new McpSecretReferences(
                provider.GetRequiredService<IMcpOperationPolicy>(), registry,
                timeProvider ?? provider.GetService<TimeProvider>() ?? TimeProvider.System,
                aliases, issuanceStore, issuanceStorePath, maxIssuedReferences, maxSecretBytes,
                provider.GetService<ITelemetryContext>()));
            return services;
        }
    }
}
