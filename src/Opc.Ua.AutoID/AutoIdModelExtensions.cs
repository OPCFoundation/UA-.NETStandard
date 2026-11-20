// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using Opc.Ua.Di;

namespace Opc.Ua.AutoID
{
    /// <summary>
    /// Registers AutoID and its Device Integration dependency without a server or client runtime reference.
    /// </summary>
    public static class AutoIdModelExtensions
    {
        /// <summary>
        /// Adds the DI and AutoID node states, registering missing namespaces.
        /// Existing roots with the same NodeId are retained, making repeated and composed loading idempotent.
        /// </summary>
        /// <param name="nodes">The destination predefined-node collection.</param>
        /// <param name="context">The context that owns the namespace table.</param>
        /// <returns>The original collection.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="nodes"/> or <paramref name="context"/> is <c>null</c>.
        /// </exception>
        public static NodeStateCollection AddAutoIdTypeSystem(this NodeStateCollection nodes, ISystemContext context)
        {
            if (nodes is null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            foreach (string namespaceUri in AutoIdModel.GetNamespaceUris())
            {
                context.NamespaceUris.GetIndexOrAppend(namespaceUri);
            }

            var additions = new NodeStateCollection();
            additions.AddOpcUaDi(context)
                .AddOpcUaAutoID(context);

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
        /// Registers the DI and AutoID encodeables.
        /// The caller commits the returned builder and owns its lifetime.
        /// </summary>
        /// <param name="builder">The encodeable factory builder.</param>
        /// <returns>The builder for further registration or commit.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public static IEncodeableFactoryBuilder AddAutoIdTypeSystem(this IEncodeableFactoryBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddOpcUaDi()
                .AddOpcUaAutoID();
        }

        /// <summary>
        /// Registers the typed DI and AutoID node-state factories.
        /// </summary>
        /// <param name="builder">The node-state factory builder.</param>
        /// <returns>The builder for further registration.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is <c>null</c>.
        /// </exception>
        public static INodeStateFactoryBuilder AddAutoIdTypeSystem(this INodeStateFactoryBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            return builder.AddOpcUaDi()
                .AddOpcUaAutoID();
        }
    }
}
