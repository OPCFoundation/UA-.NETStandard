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
        /// Existing roots are retained and types follow their supertypes in the collection.
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

            var pending = new List<NodeState>(nodes);
            var indexes = new Dictionary<NodeId, int>();
            for (int index = 0; index < pending.Count; index++)
            {
                indexes.TryAdd(pending[index].NodeId, index);
            }
            foreach (NodeState node in additions)
            {
                if (indexes.TryAdd(node.NodeId, pending.Count))
                {
                    pending.Add(node);
                }
            }


            var ordered = new List<NodeState>(pending.Count);
            var marks = new byte[pending.Count];
            for (int index = 0; index < pending.Count; index++)
            {
                AddAfterSupertype(index, pending, indexes, marks, ordered);
            }
            bool changed = nodes.Count != ordered.Count;
            for (int index = 0; !changed && index < nodes.Count; index++)
            {
                changed = !ReferenceEquals(nodes[index], ordered[index]);
            }
            if (changed)
            {
                nodes.Clear();
                nodes.AddRange(ordered);
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

        /// <summary>
        /// Adds a root after its supertype, retaining the original node instance.
        /// </summary>
        private static void AddAfterSupertype(
            int index,
            List<NodeState> pending,
            Dictionary<NodeId, int> indexes,
            byte[] marks,
            List<NodeState> ordered)
        {
            if (marks[index] == 2)
            {
                return;
            }
            if (marks[index] == 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The type hierarchy contains a cycle.");
            }
            marks[index] = 1;
            if (pending[index] is BaseTypeState type &&
                !type.SuperTypeId.IsNull &&
                indexes.TryGetValue(type.SuperTypeId, out int supertypeIndex))
            {
                AddAfterSupertype(supertypeIndex, pending, indexes, marks, ordered);
            }
            marks[index] = 2;
            ordered.Add(pending[index]);
        }
    }
}
