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
