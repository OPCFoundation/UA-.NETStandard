// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Fluent builder for an existing DI <see cref="TopologyElementState"/>
    /// or companion-spec subtype.
    /// </summary>
    /// <typeparam name="TElement">Concrete topology-element state type.</typeparam>
    public interface ITopologyElementBuilder<TElement>
        where TElement : TopologyElementState
    {
        /// <summary>
        /// The topology-element state being configured.
        /// </summary>
        TElement Element { get; }

        /// <summary>
        /// Manager that owns the element's predefined-node registration.
        /// </summary>
        DiNodeManager Manager { get; }

        /// <summary>
        /// The system context for property/reference resolution.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// A typed fluent-node view of the topology element.
        /// </summary>
        INodeBuilder<TElement> Node { get; }

    }
}
