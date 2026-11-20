/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.Programs;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Opt-in registration of managed OPC UA Part 10 Programs without starting workloads.
    /// </summary>
    public static class ProgramServerBuilderExtensions
    {
        /// <summary>
        /// Adds one per-server Program host with explicitly registered concrete types.
        /// </summary>
        public static IOpcUaServerBuilder AddPrograms(
            this IOpcUaServerBuilder builder,
            ArrayOf<ProgramDefinition> definitions,
            string instanceNamespaceUri = ProgramNodeManager.DefaultInstanceNamespaceUri,
            ArrayOf<ExpandedNodeId> addNodesTypes = default,
            Func<ISystemContext, NodeStateCollection>? modelLoader = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var factory = new ProgramNodeManagerFactory(
                definitions, instanceNamespaceUri, addNodesTypes, modelLoader);
            RegisterInfrastructure(builder);
            return builder.AddNodeManager((IAsyncNodeManagerFactory)factory);
        }

        /// <summary>
        /// Resolves Program providers and definitions from dependency injection once per server start.
        /// </summary>
        public static IOpcUaServerBuilder AddPrograms(
            this IOpcUaServerBuilder builder,
            Func<IServiceProvider, ArrayOf<ProgramDefinition>> definitions,
            string instanceNamespaceUri = ProgramNodeManager.DefaultInstanceNamespaceUri,
            ArrayOf<ExpandedNodeId> addNodesTypes = default,
            Func<ISystemContext, NodeStateCollection>? modelLoader = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (definitions is null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }
            RegisterInfrastructure(builder);
            return builder.AddNodeManagers((services, _) =>
                [new ProgramNodeManagerFactory(
                    definitions(services), instanceNamespaceUri, addNodesTypes, modelLoader)]);
        }

        private static void RegisterInfrastructure(IOpcUaServerBuilder builder)
        {
            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                if (descriptor.ServiceType == typeof(ProgramRegistration))
                {
                    throw new InvalidOperationException(
                        "Register Programs once with the complete set of concrete type definitions.");
                }
            }
            builder.Services.AddSingleton(new ProgramRegistration());
        }

        private sealed class ProgramRegistration;
    }
}
