// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Recycling.Model;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Loads the compiled sample types and exactly the instance appropriate to the selected host role.
    /// </summary>
    public sealed class RecyclingModelProvider : IMachineryModelProvider
    {
        /// <summary>
        /// Creates the model contribution for a single owner.
        /// </summary>
        public RecyclingModelProvider(RecyclingHostOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public int Order => 100;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => [RecyclingModelMapping.NamespaceUri];

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            ArgumentNullException.ThrowIfNull(nodes);
            ArgumentNullException.ThrowIfNull(context);
            context.EncodeableFactory.Builder.AddOpcUaRecyclingModel().Commit();
            context.NodeStateFactory.AddOpcUaRecyclingModel();
            var model = new NodeStateCollection();
            model.AddOpcUaRecyclingModel(context);
            uint omitted = m_options.Role == RecyclingHostRole.Coordinator
                ? Model.Objects.ProcessView
                : Model.Objects.Coordinator;
            ushort namespaceIndex = context.NamespaceUris.GetIndexOrAppend(RecyclingModelMapping.NamespaceUri);
            var omittedId = new NodeId(omitted, namespaceIndex);
            foreach (NodeState node in model)
            {
                if (node.NodeId != omittedId)
                {
                    nodes.Add(node);
                }
            }
        }

        private readonly RecyclingHostOptions m_options;
    }
}
