// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using Microsoft.Extensions.Options;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PackML;

namespace Opc.Ua.Scales.Server
{
    /// <summary>
    /// Adds PackML and Scales after the Machinery provider's shared dependency models.
    /// </summary>
    public sealed class ScalesMachineryModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates a provider with optional additional namespace registrations.
        /// </summary>
        public ScalesMachineryModelProvider(IOptions<ScalesServerOptions>? options = null)
        {
            m_options = (options?.Value ?? new ScalesServerOptions()).Validate();
        }

        /// <inheritdoc/>
        public int Order => 200;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris =>
            ((ArrayOf<string>)[PackML.Namespaces.PackML, Namespaces.Scales])
                .AddItems(m_options.AdditionalNamespaceUris);

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
                    "Scales composition requires MachineryParts.BuildingBlocks on the owner.");
            }
            NodeId packMlType = ExpandedNodeId.ToNodeId(
                PackML.ObjectTypeIds.PackMLBaseStateMachineType, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == packMlType))
            {
                nodes.AddOpcUaPackML(context);
            }
            NodeId scaleType = ExpandedNodeId.ToNodeId(ObjectTypeIds.ScaleDeviceType, context.NamespaceUris);
            if (!nodes.Any(node => node.NodeId == scaleType))
            {
                nodes.AddOpcUaScales(context);
            }
            context.EncodeableFactory.Builder.AddOpcUaPackML().AddOpcUaScales().Commit();
        }

        /// <summary>The selected namespace options.</summary>
        private readonly ScalesServerOptions m_options;
    }
}
