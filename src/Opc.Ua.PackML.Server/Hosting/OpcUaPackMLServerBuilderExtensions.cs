// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.PackML.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.NodeManager;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers standalone PackML hosting without Device Integration or Scales model dependencies.
    /// </summary>
    public static class OpcUaPackMLServerBuilderExtensions
    {
        /// <summary>
        /// Registers PackML units using explicitly supplied equipment providers.
        /// </summary>
        public static IOpcUaServerBuilder AddPackML(
            this IOpcUaServerBuilder builder,
            Action<PackMLServerOptions> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return builder.AddPackML((services, options) => configure(options));
        }

        /// <summary>
        /// Registers units with providers resolved from the application's service
        /// container. The container retains provider ownership.
        /// </summary>
        public static IOpcUaServerBuilder AddPackML(
            this IOpcUaServerBuilder builder,
            Action<IServiceProvider, PackMLServerOptions> configure)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            builder.Services.AddSingleton(services =>
            {
                var options = new PackMLServerOptions();
                configure(services, options);
                options.Validate();
                return new OpcUaServerNodeManagerRegistration(new PackMLFactory(options));
            });
            return builder;
        }

        /// <summary>
        /// Captures the configured owner factory for each server instance.
        /// </summary>
        private sealed class PackMLFactory : IAsyncNodeManagerFactory
        {
            /// <summary>
            /// Creates a factory borrowing the configured provider registrations.
            /// </summary>
            public PackMLFactory(PackMLServerOptions options)
            {
                m_options = options;
            }

            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => [m_options.NamespaceUri, Opc.Ua.PackML.Namespaces.PackML];

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The server owns the returned manager. TODO: teach CA2000 the factory ownership contract.
#pragma warning disable CA2000
                return new ValueTask<IAsyncNodeManager>(new PackMLNodeManager(server, configuration, m_options));
#pragma warning restore CA2000
            }

            /// <summary>The provider registrations and node-manager startup options.</summary>
            private readonly PackMLServerOptions m_options;
        }
    }
}
