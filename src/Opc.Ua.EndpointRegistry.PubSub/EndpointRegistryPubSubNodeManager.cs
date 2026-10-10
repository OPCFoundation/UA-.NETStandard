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
using Opc.Ua.PubSub.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.EndpointRegistry.PubSub
{
    /// <summary>
    /// Hosts the optional binding facet after existing Endpoint Registry and PubSub node managers.
    /// Has no independent catalog state or transport runtime.
    /// </summary>
    public sealed class EndpointRegistryPubSubNodeManager : AsyncCustomNodeManager
    {
        /// <summary>Creates an integration manager. Register it after its two existing managers.</summary>
        public EndpointRegistryPubSubNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            EndpointRegistryPubSubBindingOptions? options = null)
            : base(server, configuration, server.Telemetry.CreateLogger<EndpointRegistryPubSubNodeManager>(),
                "http://opcfoundation.org/UA/EndpointRegistry/PubSub")
        {
            m_options = options ?? new EndpointRegistryPubSubBindingOptions();
        }

        /// <summary>Gets the active facet, or null before address-space creation.</summary>
        public EndpointRegistryPubSubBinding? Binding { get; private set; }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            EndpointRegistryNodeManager? registry = null;
            PubSubNodeManager? pubsub = null;
            foreach (IAsyncNodeManager manager in Server.NodeManager.AsyncNodeManagers)
            {
                if (manager is EndpointRegistryNodeManager endpoint)
                {
                    registry = endpoint;
                }
                if (manager is PubSubNodeManager publisher)
                {
                    pubsub = publisher;
                }
            }
            if (registry is null || pubsub is null || registry.Generic is null)
            {
                throw new InvalidOperationException("Register the binding facet after initialized Endpoint Registry and PubSub managers.");
            }
            var context = new ServiceMessageContext(Server.Telemetry, Server.Factory)
            {
                NamespaceUris = Server.NamespaceUris,
                ServerUris = Server.ServerUris
            };
            Binding = new EndpointRegistryPubSubBinding(registry, pubsub, context, Server.Telemetry, m_options);
            await Binding.StartAsync(cancellationToken).ConfigureAwait(false);
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

        private readonly EndpointRegistryPubSubBindingOptions m_options;
    }

    /// <summary>Factory for direct construction and hosted-server DI registration of the facet.</summary>
    public sealed class EndpointRegistryPubSubNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>Creates a factory with explicit server-owned binding options.</summary>
        public EndpointRegistryPubSubNodeManagerFactory(EndpointRegistryPubSubBindingOptions? options = null)
        {
            m_options = options ?? new EndpointRegistryPubSubBindingOptions();
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => ["http://opcfoundation.org/UA/EndpointRegistry/PubSub"];

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server, ApplicationConfiguration configuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA2000 // The server takes ownership of the manager.
            return new ValueTask<IAsyncNodeManager>(new EndpointRegistryPubSubNodeManager(server, configuration, m_options));
#pragma warning restore CA2000
        }

        private readonly EndpointRegistryPubSubBindingOptions m_options;
    }

    /// <summary>DI composition for the optional PubSub Binding facet.</summary>
    public static class EndpointRegistryPubSubServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the integration manager on an existing regular OPC UA server. Call after
        /// registering the Endpoint Registry and PubSub server managers. Does not add a runtime.
        /// </summary>
        public static IOpcUaServerBuilder AddEndpointRegistryPubSubBinding(
            this IOpcUaServerBuilder builder, EndpointRegistryPubSubBindingOptions? options = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            builder.Services.AddSingleton(new OpcUaServerNodeManagerRegistration(
                new EndpointRegistryPubSubNodeManagerFactory(options)));
            return builder;
        }
    }
}
