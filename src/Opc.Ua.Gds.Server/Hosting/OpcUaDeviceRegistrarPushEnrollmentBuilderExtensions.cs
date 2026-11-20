// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Gds.Server.Onboarding;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Selects durable push request orchestration through the host's existing approved GDS certificate service.
    /// </summary>
    public static class OpcUaDeviceRegistrarPushEnrollmentBuilderExtensions
    {
        /// <summary>
        /// Adds one exclusive enrollment journal bound to this host's actual GDS application/request namespaces.
        /// Secret storage, lifecycle and the shared issuance receipt store must be configured explicitly.
        /// No automatic software approval or new authority is registered.
        /// </summary>
        public static IGdsServerBuilder AddDeviceRegistrarPushEnrollments(
            this IGdsServerBuilder builder,
            string scope,
            ExpandedNodeId certificateGroupId,
            NodeId certificateTypeId)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                if (!descriptor.IsKeyedService &&
                    (descriptor.ServiceType == typeof(IDeviceRegistrarPushEnrollmentProvider) ||
                    descriptor.ServiceType == typeof(SecretStoreDeviceRegistrarPushEnrollmentProvider)))
                {
                    throw new InvalidOperationException("Select only one registrar push enrollment provider.");
                }
            }
            builder.Services.AddSingleton(provider => new SecretStoreDeviceRegistrarPushEnrollmentProvider(
                provider.GetRequiredService<ISecretStore>(), scope, certificateGroupId, certificateTypeId,
                provider.GetRequiredService<IDeviceRegistrarIdentityLifecycle>(),
                provider.GetRequiredService<IDeviceRegistrarCertificateLifecycle>(),
                provider.GetRequiredService<ITelemetryContext>(),
                provider.GetService<IDeviceRegistrarExternalAuthenticationProvider>()));
            builder.Services.AddSingleton<IDeviceRegistrarPushEnrollmentProvider>(provider =>
                provider.GetRequiredService<SecretStoreDeviceRegistrarPushEnrollmentProvider>());
            return builder;
        }
    }
}
