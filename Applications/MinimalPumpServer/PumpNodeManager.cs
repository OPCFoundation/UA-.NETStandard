/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Di.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Pumps
{
    /// <summary>
    /// Hand-written node manager partial that provides the infrastructure
    /// (constructor, address-space load, fluent builder wiring) for the
    /// OPC 40223 Pumps companion specification server.
    /// </summary>
    public partial class PumpNodeManager : DiNodeManager
    {
        /// <summary>
        /// Initialises a new <see cref="PumpNodeManager"/>.
        /// </summary>
        public PumpNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(
                  server,
                  configuration,
                  PumpsNamespaceUri,
                  MachineryNamespaceUri)
        {
            // Base class constructor sets SystemContext.NodeIdFactory to
            // itself; our New() override takes over.
            SystemContext.NodeIdFactory = this;
        }

        private const string PumpsNamespaceUri = "http://opcfoundation.org/UA/Pumps/";
        private const string MachineryNamespaceUri = "http://opcfoundation.org/UA/Machinery/";

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance &&
                instance.Parent != null)
            {
                string parentId = instance.Parent.NodeId.IdentifierAsString;
                return new NodeId(
                    $"{parentId}_{instance.SymbolicName}",
                    instance.Parent.NodeId.NamespaceIndex);
            }

            return node.NodeId;
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            // Multi-model composition via the G5 IModelLoaderBuilder:
            // - DI types come from the Opc.Ua.Di library (AddOpcUaDi extension).
            // - Machinery + Pumps are loaded at runtime from embedded NodeSet2
            //   XMLs because their source-gen output would reference
            //   'global::DI.*' types that don't exist in our 'Opc.Ua.Di'
            //   namespace mapping.
            Assembly assembly = typeof(PumpNodeManager).Assembly;
            NodeStateCollection nodes = new ModelLoaderBuilder()
                .AddModel((coll, ctx) => coll.AddOpcUaDi(ctx))
                .ImportEmbeddedNodeSet(assembly, "Opc.Ua.Machinery.NodeSet2.xml")
                .ImportEmbeddedNodeSet(assembly, "Opc.Ua.Pumps.NodeSet2.xml")
                .Build(new NodeStateCollection(), context);

            return new ValueTask<NodeStateCollection>(nodes);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(
                externalReferences, cancellationToken).ConfigureAwait(false);

            // Build the fluent wiring surface and invoke Configure.
            ushort nsIndex = (ushort)Server.NamespaceUris.GetIndex(PumpsNamespaceUri);
            NodeManagerBuilder builder = new NodeManagerBuilder(
                SystemContext,
                this,
                nsIndex,
                browseName => FindRootByBrowseName(browseName)!,
                nodeId => FindNodeById(nodeId)!,
                typeDefId => FindNodesByTypeId(typeDefId));

            // Attach FluentNodeManagerBase registries (event sources +
            // simulation loops) so the fluent .Publish() and .Simulation()
            // extensions can find them.
            AttachToBuilder(builder);

            Configure(builder);
            builder.Seal();

            m_logger.LogInformation(
                "PumpNodeManager: address space ready ({NodeCount} predefined nodes).",
                PredefinedNodes.Count);
        }

        private NodeState? FindRootByBrowseName(QualifiedName browseName)
        {
            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node.BrowseName == browseName)
                {
                    return node;
                }
            }
            return null;
        }

        private NodeState? FindNodeById(NodeId nodeId)
        {
            return PredefinedNodes.TryGetValue(nodeId, out NodeState? node) ? node : null;
        }

        private List<NodeState> FindNodesByTypeId(NodeId typeDefinitionId)
        {
            List<NodeState> results = new List<NodeState>();
            foreach (NodeState node in PredefinedNodes.Values)
            {
                if (node is BaseInstanceState instance &&
                    instance.TypeDefinitionId == typeDefinitionId)
                {
                    results.Add(node);
                }
            }
            return results;
        }

        /// <summary>Partial wired by the Configure.cs sibling.</summary>
        partial void Configure(INodeManagerBuilder builder);
    }

    /// <summary>
    /// Factory that produces <see cref="PumpNodeManager"/> instances.
    /// </summary>
    public sealed class PumpNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => new string[]
        {
            "http://opcfoundation.org/UA/Pumps/",
            "http://opcfoundation.org/UA/Machinery/",
            global::Opc.Ua.Di.Namespaces.OpcUaDi
        };

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership transferred to server.")]
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            IAsyncNodeManager nm = new PumpNodeManager(server, configuration);
            return new ValueTask<IAsyncNodeManager>(nm);
        }
    }
}
