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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Hosting;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers independent application runtimes for standard certificate and trust push management.
    /// </summary>
    public static class OpcUaManagedApplicationPushBuilderExtensions
    {
        /// <summary>
        /// Selects one explicit runtime provider for the GDS ManagedApplications folder.
        /// This does not register software approval, metadata-only stores or a new certificate manager.
        /// </summary>
        public static IGdsServerBuilder AddManagedApplicationPush(
            this IGdsServerBuilder builder,
            Func<IServiceProvider, IManagedApplicationPushProvider> factory)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            RegisterProvider(builder.Services, factory);
            return builder;
        }

        /// <summary>
        /// Selects independent application push management on a regular server.
        /// Device hosts bind applicable configurations to their existing ProvisionableDevice before publication.
        /// </summary>
        public static IOpcUaServerBuilder AddManagedApplicationPush(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, IManagedApplicationPushProvider> factory)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            RegisterProvider(builder.Services, factory);
            return builder.AddNodeManagers((services, _) =>
            [
                new ManagedApplicationsFactory(services.GetRequiredService<IManagedApplicationPushProvider>())
            ]);
        }

        /// <summary>
        /// Fails on ambiguous provider ownership rather than silently selecting the last registration.
        /// </summary>
        private static void RegisterProvider(
            IServiceCollection services,
            Func<IServiceProvider, IManagedApplicationPushProvider> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            foreach (ServiceDescriptor descriptor in services)
            {
                if (!descriptor.IsKeyedService && descriptor.ServiceType == typeof(IManagedApplicationPushProvider))
                {
                    throw new InvalidOperationException("Select only one managed-application runtime provider.");
                }
            }
            services.AddSingleton(factory);
        }

        /// <summary>
        /// Creates a server-owned application node manager without reflection-based activation.
        /// </summary>
        private sealed class ManagedApplicationsFactory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Retains the host-owned provider.
            /// </summary>
            public ManagedApplicationsFactory(IManagedApplicationPushProvider provider)
            {
                m_provider = provider;
            }

            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris =>
                [DefaultManagedApplicationsNodeManager.ManagedApplicationsNamespaceUri];

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // MasterNodeManager owns and disposes this manager; no push bindings exist before it does.
                // TODO: Remove this suppression when CA2000 recognizes ownership through ValueTask.
#pragma warning disable CA2000 // Ownership transfers to MasterNodeManager; see the comment above.
                return new ValueTask<IAsyncNodeManager>(
                    new DefaultManagedApplicationsNodeManager(server, configuration, m_provider));
#pragma warning restore CA2000
            }

            /// <summary>The host-owned runtime provider.</summary>
            private readonly IManagedApplicationPushProvider m_provider;
        }
    }
}
