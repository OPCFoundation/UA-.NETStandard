/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Build context shared by Robotics hosting configurators.
    /// </summary>
    public interface IRoboticsBuildContext
    {
        /// <summary>
        /// Gets the active DI node manager.
        /// </summary>
        DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the active system context.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// Gets the single fluent node-manager builder owned by this context.
        /// </summary>
        INodeManagerBuilder Nodes { get; }

        /// <summary>
        /// Gets the application-owned instance namespace index.
        /// </summary>
        ushort InstanceNamespaceIndex { get; }

        /// <summary>
        /// Gets the DI DeviceSet node.
        /// </summary>
        NodeState DeviceSet { get; }

        /// <summary>
        /// Gets the hosting cancellation token.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves a required application service.
        /// </summary>
        /// <typeparam name="T">
        /// The required service contract.
        /// </typeparam>
        T GetRequiredService<T>() where T : notnull;

        /// <summary>
        /// Seals the builder, completes the registrations that could not
        /// finish synchronously and starts configured simulations.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask SealAsync(CancellationToken cancellationToken = default);
    }
}
