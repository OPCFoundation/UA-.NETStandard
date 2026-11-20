// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Opc.Ua.Di.Server;
using Opc.Ua.IRDI;

namespace Opc.Ua.PADIM.Server
{
    /// <summary>
    /// Loads the sole compiled PA-DIM and IRDI models into any DI address-space owner.
    /// </summary>
    public sealed class PadimModelProvider : IDiModelProvider
    {
        /// <inheritdoc/>
        public int Order => 30081;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => [Namespaces.PADIM, IRDI.Namespaces.IRDI];

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
            var compiled = new NodeStateCollection();
            compiled.AddOpcUaIRDI(context);
            compiled.AddOpcUaPADIM(context);
            var existing = new HashSet<NodeId>(nodes.Select(node => node.NodeId));
            foreach (NodeState node in compiled)
            {
                if (existing.Add(node.NodeId))
                {
                    nodes.Add(node);
                }
            }
        }
    }
}
