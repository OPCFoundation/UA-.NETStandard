/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.Di.Server;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Presents a plain <see cref="IDiModelProvider"/> to the Machinery provider pipeline.
    /// </summary>
    internal sealed class DiModelProviderAdapter : IMachineryModelProvider
    {
        /// <summary>
        /// Creates an adapter for the registered provider instance.
        /// </summary>
        public DiModelProviderAdapter(IDiModelProvider provider)
        {
            m_provider = provider;
        }

        /// <summary>
        /// Gets the wrapped provider's model loading order.
        /// </summary>
        public int Order => m_provider.Order;

        /// <summary>
        /// Gets the namespaces contributed by the wrapped provider.
        /// </summary>
        public ArrayOf<string> NamespaceUris => m_provider.NamespaceUris;

        /// <summary>
        /// Gets the original provider type used to break model ordering ties.
        /// </summary>
        internal System.Type ProviderType => m_provider.GetType();

        /// <summary>
        /// Loads predefined nodes through the wrapped provider.
        /// </summary>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            m_provider.AddPredefinedNodes(nodes, context);
        }

        /// <summary>The registered provider instance.</summary>
        private readonly IDiModelProvider m_provider;
    }
}
