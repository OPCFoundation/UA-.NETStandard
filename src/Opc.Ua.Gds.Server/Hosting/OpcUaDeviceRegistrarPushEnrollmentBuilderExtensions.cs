/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

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
