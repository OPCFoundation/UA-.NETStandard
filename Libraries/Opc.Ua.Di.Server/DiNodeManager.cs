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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// OPC UA node manager that exposes the Device Integration (DI) model
    /// (OPC 10000-100) predefined nodes.
    /// </summary>
    /// <remarks>
    /// The static model nodes — all type definitions, reference types, and
    /// well-known instances defined by the DI companion specification — are
    /// loaded from the source-generated <c>AddDi</c> extension.
    /// Subclasses can override
    /// <see cref="AsyncCustomNodeManager.AddBehaviourToPredefinedNodeAsync"/>
    /// to attach live behaviour to specific predefined nodes.
    /// </remarks>
    public class DiNodeManager : AsyncCustomNodeManager, INodeIdFactory
    {
        /// <summary>
        /// The DI namespace URI (<c>http://opcfoundation.org/UA/DI/</c>).
        /// </summary>
        public const string DiNamespaceUri = global::Opc.Ua.Di.Namespaces.OpcUaDi;

        /// <summary>
        /// Initialises a new <see cref="DiNodeManager"/>.
        /// </summary>
        public DiNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(
                  server,
                  configuration,
                  server.Telemetry.CreateLogger<DiNodeManager>(),
                  DiNamespaceUri)
        {
            SystemContext.NodeIdFactory = this;
        }

        /// <summary>The namespace index of the DI model.</summary>
        public ushort DiNamespaceIndex =>
            (ushort)Server.NamespaceUris.GetIndex(DiNamespaceUri);

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance &&
                instance.Parent != null)
            {
                string parentId = instance.Parent.NodeId.IdentifierAsString;

                return new NodeId(
                    $"{parentId}_{instance.SymbolicName}",
                    DiNamespaceIndex);
            }

            return node.NodeId;
        }

        /// <summary>
        /// Adds an instance node under the Machinery <c>Machines</c> folder
        /// if it is present in the address space. Returns <see langword="true"/>
        /// when the folder was found and the reference was added.
        /// </summary>
        protected bool TryAddToMachinesFolder(BaseInstanceState instance)
        {
            // The well-known Machines folder BrowseName defined by the
            // Machinery companion specification (OPC 40001).
            const string machinesFolderBrowseName = "Machines";

            foreach (NodeState root in PredefinedNodes.Values)
            {
                if (root.BrowseName.Name == machinesFolderBrowseName &&
                    root is BaseObjectState folder)
                {
                    folder.AddChild(instance);
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<NodeStateCollection>(
                new NodeStateCollection().AddOpcUaDi(context));
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(
                externalReferences, cancellationToken).ConfigureAwait(false);
        }
    }
}
