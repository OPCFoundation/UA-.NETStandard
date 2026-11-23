// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Registers the fixed namespace and assignment contract shared by a replica set.
    /// </summary>
    public static class ReplicaNodeIdentityBuilderExtensions
    {
        /// <summary>
        /// Configures identical shared NodeIds before node managers are constructed.
        /// </summary>
        /// <param name="builder">The server builder.</param>
        /// <param name="replicaSetId">Stable identity of the replica set.</param>
        /// <param name="namespaceUris">Ordered shared namespaces; indexes start at two.</param>
        /// <param name="mode">Deterministic mode for independently created named nodes.</param>
        /// <param name="writerAssignedIds">
        /// Enables counter allocations only on the active/passive election's writer.
        /// Leave false for active/active graphs.
        /// </param>
        /// <returns>The same builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        public static IOpcUaServerBuilder UseReplicaNodeIdentity(
            this IOpcUaServerBuilder builder,
            string replicaSetId,
            ArrayOf<string> namespaceUris,
            NodeIdAssignmentMode mode = NodeIdAssignmentMode.Numeric,
            bool writerAssignedIds = false)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var settings = new ReplicaNodeIdFactory(replicaSetId, namespaceUris, mode);
            builder.Services.Replace(ServiceDescriptor.Singleton(new ReplicaSetEventIdentitySeed(replicaSetId)));
            builder.Services.Replace(ServiceDescriptor.Singleton(services =>
            {
                bool activeActive = services.GetService<ReplicatedAddressSpaceOptions>() != null;
                if (activeActive &&
                    (writerAssignedIds || services.GetService<DistributedAddressSpaceOptions>() != null))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError,
                        "Active/active address spaces require independent identities " +
                        "and cannot also use writer replication.");
                }
                return new ReplicaNodeIdFactory(
                    replicaSetId,
                    settings.NamespaceUris,
                    mode,
                    writerAssignedIds ? services.GetRequiredService<ILeaderElection>() : null,
                    activeActive ? null : services.GetService<ISharedKeyValueStore>(),
                    services.GetService<IRecordProtector>());
            }));
            builder.Services.Replace(ServiceDescriptor.Singleton<IRebasableNodeIdFactory>(
                services => services.GetRequiredService<ReplicaNodeIdFactory>()));
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IStrongKeyspaceProvider, IdentityKeyspaceProvider>());
            return builder;
        }

        private sealed class IdentityKeyspaceProvider : IStrongKeyspaceProvider
        {
            public ArrayOf<string> GetStrongKeyPrefixes()
            {
                return [ReplicaIdentityStore.Key];
            }
        }
    }

    /// <summary>
    /// Shared identity from the replica node identity registration, independent of node factory initialization.
    /// </summary>
    internal sealed record ReplicaSetEventIdentitySeed(string Value);
}
