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
using Opc.Ua.AMB;
using Opc.Ua.Di;
using Opc.Ua.IA;
using Opc.Ua.Machinery;

namespace Opc.Ua.LADS
{
    /// <summary>
    /// Registers LADS and its DI, IA, Machinery and AMB dependencies without a server or client runtime reference.
    /// </summary>
    public static class LadsModelExtensions
    {
        /// <summary>
        /// Adds the DI, IA, Machinery, AMB and LADS node states, registering missing namespaces.
        /// Existing roots with the same NodeId are retained, making repeated and composed loading idempotent.
        /// </summary>
        /// <param name="nodes">The destination predefined-node collection.</param>
        /// <param name="context">The context that owns the namespace table.</param>
        /// <returns>The original collection.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodes"/> or <paramref name="context"/> is <c>null</c>.
        /// </exception>
        public static NodeStateCollection AddLadsTypeSystem(this NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes is null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            foreach (string namespaceUri in LadsModel.GetNamespaceUris())
            {
                context.NamespaceUris.GetIndexOrAppend(namespaceUri);
            }

            var additions = new NodeStateCollection();
            additions.AddOpcUaDi(context)
                .AddOpcUaIA(context)
                .AddOpcUaMachinery(context)
                .AddOpcUaAMB(context)
                .AddOpcUaLADS(context);

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
        /// Registers the DI, IA, AMB and LADS encodeables; Machinery defines no DataTypes.
        /// The caller commits the returned builder and owns its lifetime.
        /// </summary>
        /// <param name="builder">The encodeable factory builder.</param>
        /// <returns>The builder for further registration or commit.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public static IEncodeableFactoryBuilder AddLadsTypeSystem(this IEncodeableFactoryBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddOpcUaDi()
                .AddOpcUaIA()
                .AddOpcUaAMB()
                .AddOpcUaLADS();
        }

        /// <summary>
        /// Registers the typed DI, IA, Machinery, AMB and LADS node-state factories.
        /// </summary>
        /// <param name="builder">The node-state factory builder.</param>
        /// <returns>The builder for further registration.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public static INodeStateFactoryBuilder AddLadsTypeSystem(this INodeStateFactoryBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddOpcUaDi()
                .AddOpcUaIA()
                .AddOpcUaMachinery()
                .AddOpcUaAMB()
                .AddOpcUaLADS();
        }
    }
}