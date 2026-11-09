/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
