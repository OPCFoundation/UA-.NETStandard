// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Fluent helper returned by
    /// <see cref="OpcUaServerBuilderExtensions.AddServer(IOpcUaBuilder,Action{OpcUaServerOptions})"/>;
    /// allows chained registration of node-manager factories.
    /// </summary>
    public interface IOpcUaServerBuilder : IOpcUaTransportBuilder
    {
        /// <summary>
        /// Registers an asynchronous node-manager factory as a singleton.
        /// </summary>
        /// <typeparam name="TFactory"></typeparam>
        IOpcUaServerBuilder AddNodeManager<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>()
            where TFactory : class, IAsyncNodeManagerFactory;

        /// <summary>
        /// Registers a fluent node manager built from a namespace URI and configuration callback.
        /// The callback creates and places every node; no implicit root node is added.
        /// </summary>
        /// <param name="namespaceUri">Namespace URI owned by the fluent node manager.</param>
        /// <param name="build">Callback that wires fluent nodes, alarms, state machines and simulations.</param>
        IOpcUaServerBuilder AddNodeManager(string namespaceUri, Action<INodeManagerBuilder> build);

        /// <summary>
        /// Registers a synchronous (legacy) node-manager factory as a singleton.
        /// </summary>
        /// <typeparam name="TFactory">The node manager factory type.</typeparam>
        IOpcUaServerBuilder AddSyncNodeManager<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFactory>()
            where TFactory : class, INodeManagerFactory;
    }
}
