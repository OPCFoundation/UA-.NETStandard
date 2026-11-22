// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using Microsoft.Extensions.Options;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Adds Pumps after the existing Machinery provider's dependency models.
    /// </summary>
    public sealed class PumpsMachineryModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates a provider with optional additional namespace registrations.
        /// </summary>
        public PumpsMachineryModelProvider(IOptions<PumpsServerOptions>? options = null)
        {
            m_options = (options?.Value ?? new PumpsServerOptions()).Validate();
        }

        /// <inheritdoc/>
        public int Order => 200;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
            ((ArrayOf<string>)[Namespaces.Pumps]).AddItems(m_options.AdditionalNamespaceUris);

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            NodeId machines = ExpandedNodeId.ToNodeId(Machinery.ObjectIds.Machines, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == machines))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Pumps composition requires MachineryParts.BuildingBlocks on the owner.");
            }
            NodeId pumpType = ExpandedNodeId.ToNodeId(ObjectTypeIds.PumpType, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == pumpType))
            {
                nodes.AddOpcUaPumps(context);
                context.EncodeableFactory.Builder.AddOpcUaPumps().Commit();
            }
        }

        /// <summary>The provider's selected namespace options.</summary>
        private readonly PumpsServerOptions m_options;
    }
}
