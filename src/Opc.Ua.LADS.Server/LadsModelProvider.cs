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
using Microsoft.Extensions.Options;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Adds the LADS model and its DI, IA and Machinery dependencies to the selected Machinery owner. The AMB
    /// namespace stays with the AMB node manager, which the LADS hosting registers when it is missing.
    /// </summary>
    public sealed class LadsModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates a directly constructible provider.
        /// </summary>
        public LadsModelProvider()
        {
        }

        /// <summary>
        /// Creates an injectable provider and checks that the owner selected the parts LADS needs.
        /// </summary>
        /// <param name="options">The LADS options.</param>
        /// <param name="ownerOptions">The Machinery owner's options.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadConfigurationError"/> when the owner lacks a required part.
        /// </exception>
        public LadsModelProvider(IOptions<LadsServerOptions> options, IOptions<MachineryServerOptions> ownerOptions)
        {
            LadsServerOptions selected = options?.Value ?? throw new ArgumentNullException(nameof(options));
            MachineryServerOptions owner = ownerOptions?.Value ?? throw new ArgumentNullException(nameof(ownerOptions));
            selected.Validate();
            if ((owner.Parts & selected.RequiredMachineryParts) != selected.RequiredMachineryParts)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The Machinery owner must select the building-block part required by LADS.");
            }
        }

        /// <inheritdoc/>
        public int Order => 100;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris
        {
            get
            {
                var uris = new List<string>();
                foreach (string uri in LadsModel.GetNamespaceUris())
                {
                    if (!string.Equals(uri, global::Opc.Ua.AMB.Namespaces.AMB, StringComparison.Ordinal))
                    {
                        uris.Add(uri);
                    }
                }
                return uris.ToArray();
            }
        }

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
            NodeStateCollection model = new NodeStateCollection().AddLadsTypeSystem(context);
            int ambIndex = context.NamespaceUris.GetIndex(global::Opc.Ua.AMB.Namespaces.AMB);
            var existing = new HashSet<NodeId>();
            foreach (NodeState node in nodes)
            {
                existing.Add(node.NodeId);
            }
            var selected = new Dictionary<NodeId, NodeState>();
            foreach (NodeState node in model)
            {
                if (node.NodeId.NamespaceIndex != ambIndex && existing.Add(node.NodeId))
                {
                    selected.Add(node.NodeId, node);
                }
            }
            var added = new HashSet<NodeId>();
            foreach (NodeState node in model)
            {
                AddAfterSupertype(node, selected, added, nodes);
            }
            context.EncodeableFactory.Builder.AddLadsTypeSystem().Commit();
        }

        /// <summary>
        /// Adds a node after its supertype. The LADS NodeSet declares some types, such as
        /// <c>SetType</c>, after their subtypes, while the server indexes the types in order.
        /// </summary>
        private static void AddAfterSupertype(
            NodeState node,
            Dictionary<NodeId, NodeState> selected,
            HashSet<NodeId> added,
            NodeStateCollection nodes)
        {
            if (!selected.ContainsKey(node.NodeId) || !added.Add(node.NodeId))
            {
                return;
            }
            if (node is BaseTypeState type &&
                !type.SuperTypeId.IsNull &&
                selected.TryGetValue(type.SuperTypeId, out NodeState? superType))
            {
                AddAfterSupertype(superType, selected, added, nodes);
            }
            nodes.Add(node);
        }
    }
}
