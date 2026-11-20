// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            foreach (NodeState node in model)
            {
                if (node.NodeId.NamespaceIndex != ambIndex && existing.Add(node.NodeId))
                {
                    nodes.Add(node);
                }
            }
            context.EncodeableFactory.Builder.AddLadsTypeSystem().Commit();
        }
    }
}
