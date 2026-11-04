/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.EndpointRegistry.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.EndpointRegistry.Federation.Server
{
    /// <summary>Opt-in lifetime integration after an existing Endpoint Registry node manager.</summary>
    public sealed class EndpointRegistryGroupFederationNodeManager : AsyncCustomNodeManager
    {
        /// <summary>Namespace used only for this integration manager, not a companion model or facet claim.</summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/EndpointRegistry/Federation";

        /// <summary>Creates an integration manager. Register it after the existing registry manager.</summary>
        public EndpointRegistryGroupFederationNodeManager(
            IServerInternal server, ApplicationConfiguration configuration, EndpointRegistryGroupFederationOptions options)
            : base(server, configuration, server.Telemetry.CreateLogger<EndpointRegistryGroupFederationNodeManager>(), NamespaceUri)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Gets the binding after address-space creation; no peer is authenticated at startup.</summary>
        public EndpointRegistryGroupFederationBinding? Binding { get; private set; }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences, CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            EndpointRegistryNodeManager? registry = null;
            foreach (IAsyncNodeManager manager in Server.NodeManager.AsyncNodeManagers)
            {
                if (manager is EndpointRegistryNodeManager candidate)
                {
                    if (registry is not null)
                    {
                        throw new InvalidOperationException("The Group binding requires an unambiguous Endpoint Registry manager.");
                    }
                    registry = candidate;
                }
            }
            if (registry is null)
            {
                throw new InvalidOperationException("Register the Group binding after an initialized Endpoint Registry manager.");
            }
            var binding = new EndpointRegistryGroupFederationBinding(registry, m_options);
            try
            {
                await binding.StartAsync(cancellationToken).ConfigureAwait(false);
                Binding = binding;
            }
            catch
            {
                await binding.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            if (Binding is { } binding)
            {
                Binding = null;
                await binding.DisposeAsync().ConfigureAwait(false);
            }
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        private readonly EndpointRegistryGroupFederationOptions m_options;
    }

    /// <summary>Factory for direct server construction and regular-server DI composition.</summary>
    public sealed class EndpointRegistryGroupFederationNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>Creates the opt-in factory with server-owned configuration.</summary>
        public EndpointRegistryGroupFederationNodeManagerFactory(EndpointRegistryGroupFederationOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => [EndpointRegistryGroupFederationNodeManager.NamespaceUri];

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server, ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000 // The regular server takes ownership of the manager.
            return new ValueTask<IAsyncNodeManager>(
                new EndpointRegistryGroupFederationNodeManager(server, configuration, m_options));
#pragma warning restore CA2000
        }

        private readonly EndpointRegistryGroupFederationOptions m_options;
    }

    /// <summary>DI registration without another registry, epoch producer or transport runtime.</summary>
    public static class EndpointRegistryGroupFederationServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the opt-in Group reference lifetime manager. Call after registering the
        /// Endpoint Registry manager. Refresh sources explicitly after startup.
        /// </summary>
        public static IOpcUaServerBuilder AddEndpointRegistryGroupFederation(
            this IOpcUaServerBuilder builder, EndpointRegistryGroupFederationOptions options)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(
                new EndpointRegistryGroupFederationNodeManagerFactory(options)));
            return builder;
        }
    }
}
