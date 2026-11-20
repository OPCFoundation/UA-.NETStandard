/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Opc.Ua.Machinery.Result;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS
{
    /// <summary>
    /// Registers GMS and its generated dependencies without a server or client runtime reference.
    /// </summary>
    public static class GmsModelExtensions
    {
        /// <summary>
        /// Adds GMS, MachineTool and Machinery Result with their dependencies, registering missing namespaces.
        /// Existing roots with the same NodeId are retained, making repeated and composed loading idempotent.
        /// WMTP is not selected through the shared MachineTool assembly.
        /// </summary>
        /// <param name="nodes">The destination predefined-node collection.</param>
        /// <param name="context">The context that owns the namespace table.</param>
        /// <returns>The original collection.</returns>
        public static NodeStateCollection AddGmsTypeSystem(this NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes is null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            foreach (string namespaceUri in GmsModel.GetNamespaceUris())
            {
                context.NamespaceUris.GetIndexOrAppend(namespaceUri);
            }

            var additions = new NodeStateCollection();
            additions.AddMachineToolTypeSystem(context, MachineToolParts.MachineTool)
                .AddOpcUaMachineryResult(context)
                .AddOpcUaGMS(context);

            var existing = new HashSet<NodeId>();
            foreach (NodeState node in nodes)
            {
                existing.Add(node.NodeId);
            }
            foreach (NodeState node in additions)
            {
                if (existing.Add(node.NodeId))
                {
                    nodes.Add(node);
                }
            }
            return nodes;
        }

        /// <summary>
        /// Registers GMS encodeables and the MachineTool/Machinery Result dependency closure.
        /// The caller commits the returned builder and owns its lifetime.
        /// </summary>
        /// <param name="builder">The encodeable factory builder.</param>
        /// <returns>The builder for further registration or commit.</returns>
        public static IEncodeableFactoryBuilder AddGmsTypeSystem(this IEncodeableFactoryBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddMachineToolTypeSystem(MachineToolParts.MachineTool)
                .AddOpcUaMachineryResult()
                .AddOpcUaGMS();
        }

        /// <summary>
        /// Registers typed GMS node-state factories and the MachineTool/Machinery Result dependency closure.
        /// </summary>
        /// <param name="builder">The node-state factory builder.</param>
        /// <returns>The builder for further registration.</returns>
        public static INodeStateFactoryBuilder AddGmsTypeSystem(this INodeStateFactoryBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddMachineToolTypeSystem(MachineToolParts.MachineTool)
                .AddOpcUaMachineryResult()
                .AddOpcUaGMS();
        }
    }
}
