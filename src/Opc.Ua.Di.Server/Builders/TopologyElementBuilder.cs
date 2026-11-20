// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Default state holder for <see cref="ITopologyElementBuilder{TElement}"/>.
    /// </summary>
    /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
    internal sealed class TopologyElementBuilder<TElement> :
        ITopologyElementBuilder<TElement>
        where TElement : TopologyElementState
    {
        private readonly NodeManagerBuilder m_builder;

        internal TopologyElementBuilder(
            DiNodeManager manager,
            TElement element,
            NodeManagerBuilder builder)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            Element = element ?? throw new ArgumentNullException(nameof(element));
            m_builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public TElement Element { get; }
        public DiNodeManager Manager { get; }
        public ISystemContext Context => Manager.SystemContext;
        public INodeBuilder<TElement> Node => m_builder.Node<TElement>(Element.NodeId);
    }
}
