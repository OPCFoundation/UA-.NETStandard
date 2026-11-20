/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Robotics.Server.Builders
{
    /// <summary>
    /// Common fluent surface for a Robotics node being assembled.
    /// </summary>
    /// <typeparam name="TState">The generated Robotics state type.</typeparam>
    public interface IRoboticsNodeBuilder<out TState>
        where TState : NodeState
    {
        /// <summary>
        /// Gets the generated state being configured.
        /// </summary>
        TState State { get; }

        /// <summary>
        /// Gets the Robotics hosting context that owns the build.
        /// </summary>
        IRoboticsBuildContext BuildContext { get; }

        /// <summary>
        /// Applies low-level state configuration before registration.
        /// </summary>
        IRoboticsNodeBuilder<TState> Configure(Action<TState, ISystemContext> configure);

        /// <summary>
        /// Resolves the registered state through the shared fluent node builder.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The containing motion-device system has not been registered.
        /// </exception>
        INodeBuilder<TState> AsNode();
    }
}
