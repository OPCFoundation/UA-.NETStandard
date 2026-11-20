/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Common fluent surface for a Machinery node being assembled.
    /// </summary>
    /// <typeparam name="TState">The generated Machinery state type.</typeparam>
    public interface IMachineryNodeBuilder<out TState>
        where TState : NodeState
    {
        /// <summary>
        /// Gets the generated state being configured.
        /// </summary>
        TState State { get; }

        /// <summary>
        /// Gets the Machinery hosting context that owns the build.
        /// </summary>
        IMachineryBuildContext BuildContext { get; }

        /// <summary>
        /// Applies low-level state configuration before registration.
        /// </summary>
        IMachineryNodeBuilder<TState> Configure(Action<TState, ISystemContext> configure);

        /// <summary>
        /// Resolves the registered state through the shared fluent node builder.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The containing machine has not been registered.
        /// </exception>
        INodeBuilder<TState> AsNode();
    }
}
