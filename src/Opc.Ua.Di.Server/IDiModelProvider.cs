/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// Contributes compiled predefined model nodes, such as the types of a
    /// DI-based companion specification, to a node manager that owns the
    /// Device Integration address space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Register a provider with <c>AddDiModel&lt;TProvider&gt;()</c>. Every
    /// DI-owning node manager (the plain <see cref="DiNodeManager"/> as well as
    /// the Machinery and Robotics managers) loads the registered providers after
    /// the DI model, so a companion model needs no knowledge of which manager
    /// hosts it.
    /// </para>
    /// <para>
    /// The DI model is loaded before a provider runs, but a provider can also be
    /// invoked by an owner that has loaded it as part of a larger model, so
    /// <see cref="AddPredefinedNodes"/> must tolerate nodes that already exist.
    /// </para>
    /// </remarks>
    public interface IDiModelProvider
    {
        /// <summary>
        /// Gets the deterministic provider execution order. Providers run in
        /// ascending order, ties broken by type name and registration order.
        /// </summary>
        int Order { get; }

        /// <summary>
        /// Gets the model namespace URIs contributed by this provider. The
        /// owning node manager registers them with the server.
        /// </summary>
        ArrayOf<string> NamespaceUris { get; }

        /// <summary>
        /// Adds compiled predefined nodes to <paramref name="nodes"/>.
        /// </summary>
        /// <param name="nodes">The collection that receives the nodes.</param>
        /// <param name="context">The system context.</param>
        void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context);
    }
}
