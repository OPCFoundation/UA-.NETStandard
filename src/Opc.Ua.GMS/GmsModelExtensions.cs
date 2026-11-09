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
