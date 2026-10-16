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
using Opc.Ua.Di;
using Opc.Ua.IA;
using Opc.Ua.IRDI;
using Opc.Ua.ISA95.JobControl.V2;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Jobs;
using Opc.Ua.Machinery.ProcessValues;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.PADIM;

namespace Opc.Ua.MachineTool
{
    /// <summary>
    /// Loads selected models and their generated dependencies without referencing a server or client runtime.
    /// </summary>
    public static class MachineToolModelExtensions
    {
        /// <summary>
        /// Adds the selected model closure to a predefined-node collection and registers its namespace URIs.
        /// Existing nodes with the same NodeId are retained, making repeated or composed loading idempotent.
        /// </summary>
        /// <param name="nodes">The destination collection.</param>
        /// <param name="context">The context that owns the namespace table.</param>
        /// <param name="parts">The models to load; None is a no-op.</param>
        /// <returns>The original collection.</returns>
        public static NodeStateCollection AddMachineToolTypeSystem(
            this NodeStateCollection nodes,
            ISystemContext context,
            MachineToolParts parts = MachineToolParts.MachineTool)
        {
            if (nodes is null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            MachineToolModels.Validate(parts);
            if (parts == MachineToolParts.None)
            {
                return nodes;
            }

            foreach (string namespaceUri in MachineToolModels.GetNamespaceUris(parts))
            {
                context.NamespaceUris.GetIndexOrAppend(namespaceUri);
            }

            var additions = new NodeStateCollection();
            additions.AddOpcUaDi(context);
            additions.AddOpcUaIA(context);
            additions.AddOpcUaMachinery(context);
            if ((parts & MachineToolParts.MachineTool) != 0)
            {
                OpcUaISA95JobControlV2Extensions.AddOpcUaISA95JobControlV2(additions, context);
                additions.AddOpcUaMachineryJobs(context);
                additions.AddOpcUaMachineTool(context);
            }
            if ((parts & MachineToolParts.Wmtp) != 0)
            {
                additions.AddOpcUaIRDI(context);
                additions.AddOpcUaPADIM(context);
                additions.AddOpcUaMachineryProcessValues(context);
                additions.AddOpcUaMachineToolWMTP(context);
            }

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
        /// Registers encodeable factories for the selected models and their dependency closure.
        /// The caller commits the returned builder and owns its lifetime.
        /// </summary>
        /// <param name="builder">The encodeable factory builder.</param>
        /// <param name="parts">The models to register; None is a no-op.</param>
        /// <returns>The builder for further registration or commit.</returns>
        public static IEncodeableFactoryBuilder AddMachineToolTypeSystem(
            this IEncodeableFactoryBuilder builder,
            MachineToolParts parts = MachineToolParts.MachineTool)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            MachineToolModels.Validate(parts);
            if (parts == MachineToolParts.None)
            {
                return builder;
            }

            builder = builder.AddOpcUaDi().AddOpcUaIA();
            if ((parts & MachineToolParts.MachineTool) != 0)
            {
                builder = OpcUaISA95JobControlV2Extensions.AddOpcUaISA95JobControlV2(builder)
                    .AddOpcUaMachineryJobs()
                    .AddOpcUaMachineTool();
            }
            if ((parts & MachineToolParts.Wmtp) != 0)
            {
                builder = builder.AddOpcUaPADIM()
                    .AddOpcUaMachineToolWMTP();
            }
            return builder;
        }

        /// <summary>
        /// Registers typed node-state factories for the selected models and their dependency closure.
        /// </summary>
        /// <param name="builder">The node-state factory builder.</param>
        /// <param name="parts">The models to register; None is a no-op.</param>
        /// <returns>The builder for further registration.</returns>
        public static INodeStateFactoryBuilder AddMachineToolTypeSystem(
            this INodeStateFactoryBuilder builder,
            MachineToolParts parts = MachineToolParts.MachineTool)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            MachineToolModels.Validate(parts);
            if (parts == MachineToolParts.None)
            {
                return builder;
            }

            builder = builder.AddOpcUaDi().AddOpcUaIA().AddOpcUaMachinery();
            if ((parts & MachineToolParts.MachineTool) != 0)
            {
                builder = OpcUaISA95JobControlV2Extensions.AddOpcUaISA95JobControlV2(builder)
                    .AddOpcUaMachineryJobs()
                    .AddOpcUaMachineTool();
            }
            if ((parts & MachineToolParts.Wmtp) != 0)
            {
                builder = builder.AddOpcUaIRDI()
                    .AddOpcUaPADIM()
                    .AddOpcUaMachineryProcessValues()
                    .AddOpcUaMachineToolWMTP();
            }
            return builder;
        }
    }
}
