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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server;
using Opc.Ua.Vision.Server.Builders;

namespace Opc.Ua.Vision.Server
{
    internal sealed class VisionBuildContext : IVisionBuildContext
    {
        public VisionBuildContext(
            AsyncCustomNodeManager manager,
            VisionRootState root,
            VisionServerOptions options,
            VisionRegistry registry,
            VisionMethodDispatcher dispatcher,
            CancellationToken cancellationToken,
            IServiceProvider? services = null)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            if (dispatcher == null)
            {
                throw new ArgumentNullException(nameof(dispatcher));
            }
            options.Validate();
            Manager = manager;
            Root = root;
            Context = manager.SystemContext;
            CancellationToken = cancellationToken;
            m_services = services;
            int instanceIndex = Context.NamespaceUris.GetIndex(options.InstanceNamespaceUri);
            if (instanceIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Vision instance namespace '{0}' is not registered.",
                    options.InstanceNamespaceUri);
            }
            InstanceNamespaceIndex = (ushort)instanceIndex;
            int visionIndex = Context.NamespaceUris.GetIndex(global::Opc.Ua.Vision.Namespaces.Vision);
            if (visionIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Vision namespace '{0}' is not registered.",
                    global::Opc.Ua.Vision.Namespaces.Vision);
            }
            VisionNamespaceIndex = (ushort)visionIndex;
            Registry = registry;
            Nodes = new VisionNodeBuilder(this, registry, dispatcher);
        }

        public AsyncCustomNodeManager Manager { get; }

        public ISystemContext Context { get; }

        public ushort InstanceNamespaceIndex { get; }

        public ushort VisionNamespaceIndex { get; }

        public VisionRootState Root { get; }

        public IVisionNodeBuilder Nodes { get; }

        internal VisionRegistry Registry { get; }

        public CancellationToken CancellationToken { get; }

        public T GetRequiredService<T>() where T : notnull
        {
            if (m_services == null)
            {
                throw new InvalidOperationException(
                    "Application services are unavailable for a directly created Vision build context.");
            }
            return m_services.GetRequiredService<T>();
        }

        internal void EnqueueForRegistration(NodeState node)
        {
            if (node == null || node.NodeId.IsNull)
            {
                return;
            }
            if (m_pendingRegistrationSet.Add(node))
            {
                m_pendingRegistrations.Add(node);
            }
        }

        internal async ValueTask FlushPendingRegistrationsAsync(CancellationToken cancellationToken)
        {
            for (int ii = 0; ii < m_pendingRegistrations.Count; ii++)
            {
                NodeState node = m_pendingRegistrations[ii];
                NormalizeInstanceMetadata(node);
                if (Manager.FindPredefinedNode<NodeState>(node.NodeId) == null)
                {
                    await Manager.AddPredefinedNodeAsync(node, cancellationToken).ConfigureAwait(false);
                }
            }
            m_pendingRegistrations.Clear();
            m_pendingRegistrationSet.Clear();
        }

        /// <summary>
        /// Gives every child a reference type and a type definition.
        /// </summary>
        /// <remarks>
        /// Delegates to <see cref="VisionNodeManager"/>, which applies the same
        /// normalization to every node it registers — including results
        /// published at runtime, which never pass through this builder. Kept
        /// here so a context whose manager is not a <c>VisionNodeManager</c>
        /// still produces valid instance nodes.
        /// </remarks>
        private void NormalizeInstanceMetadata(NodeState node)
        {
            VisionNodeManager.NormalizeInstanceMetadata(Context, node);
        }

        private readonly IServiceProvider? m_services;
        private readonly List<NodeState> m_pendingRegistrations = [];
        private readonly HashSet<NodeState> m_pendingRegistrationSet = [];
    }
}
