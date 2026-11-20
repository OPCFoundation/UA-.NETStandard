/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server;
using Opc.Ua.Vision.Perception;
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
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (dispatcher == null)
            {
                throw new ArgumentNullException(nameof(dispatcher));
            }
            options.Validate();
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            Root = root ?? throw new ArgumentNullException(nameof(root));
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
            int visionIndex = Context.NamespaceUris.GetIndex(Perception.Namespaces.Vision);
            if (visionIndex < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The Vision namespace '{0}' is not registered.",
                    Perception.Namespaces.Vision);
            }
            VisionNamespaceIndex = (ushort)visionIndex;
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (manager is Di.Server.DiNodeManager diManager)
            {
                diManager.RegisterConformanceContributor(registry);
            }
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

        /// <summary>
        /// Resolves an optional shared runtime service when hosted through dependency injection.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        internal T? GetService<T>() where T : class
        {
            return m_services?.GetService<T>();
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
