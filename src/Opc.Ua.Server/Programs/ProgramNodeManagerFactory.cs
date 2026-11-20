// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Programs
{
    /// <summary>
    /// Creates an isolated Program host for each server using standard node-manager hosting.
    /// Provider factories must return independently owned execution handles for each invocation.
    /// </summary>
    public sealed class ProgramNodeManagerFactory : INodeManagerFactory, IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a factory with explicit type policies and optional client AddNodes admission.
        /// </summary>
        public ProgramNodeManagerFactory(
            ArrayOf<ProgramDefinition> definitions,
            string instanceNamespaceUri = ProgramNodeManager.DefaultInstanceNamespaceUri,
            ArrayOf<ExpandedNodeId> addNodesTypes = default,
            Func<ISystemContext, NodeStateCollection>? modelLoader = null)
        {
            ProgramNodeManager.ValidateDefinitions(definitions, addNodesTypes);
            NamespacesUris = ProgramNodeManager.GetNamespaceUris(definitions, instanceNamespaceUri);
            m_definitions = definitions.Span.ToArray();
            m_instanceNamespaceUri = instanceNamespaceUri;
            m_addNodesTypes = addNodesTypes.Span.ToArray();
            m_modelLoader = modelLoader;
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris { get; }

        /// <inheritdoc/>
        public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
        {
            // Server owns the returned adapter and manager. TODO: model adapter ownership in analysis.
#pragma warning disable CA2000
            return CreateManager(server, configuration).SyncNodeManager;
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Server owns the returned manager. TODO: model ValueTask ownership transfer in analysis.
#pragma warning disable CA2000
            return new ValueTask<IAsyncNodeManager>(CreateManager(server, configuration));
#pragma warning restore CA2000
        }

        private ProgramNodeManager CreateManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new ProgramNodeManager(
                server, configuration, m_definitions, m_instanceNamespaceUri, m_addNodesTypes, m_modelLoader);
        }

        private readonly ArrayOf<ProgramDefinition> m_definitions;
        private readonly string m_instanceNamespaceUri;
        private readonly ArrayOf<ExpandedNodeId> m_addNodesTypes;
        private readonly Func<ISystemContext, NodeStateCollection>? m_modelLoader;
    }
}
