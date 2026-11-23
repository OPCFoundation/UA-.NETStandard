// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using Opc.Ua.RobotIntent;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Robotics.Server
{
    /// <summary>
    /// Build context used by Robot Intent configurators.
    /// </summary>
    public interface IRobotIntentBuildContext
    {
        /// <summary>
        /// Gets the active node manager.
        /// </summary>
        AsyncCustomNodeManager Manager { get; }

        /// <summary>
        /// Gets the active system context.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// Gets the fluent node builder for the instance namespace.
        /// </summary>
        INodeManagerBuilder Nodes { get; }

        /// <summary>
        /// Gets the application-owned instance namespace index.
        /// </summary>
        ushort InstanceNamespaceIndex { get; }

        /// <summary>
        /// Gets the Server/RobotIntent root object.
        /// </summary>
        RobotIntentRootState Root { get; }

        /// <summary>
        /// Gets the startup cancellation token.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves a required application service.
        /// </summary>
        /// <typeparam name="T">
        /// The service type.
        /// </typeparam>
        T GetRequiredService<T>() where T : notnull;
    }
}
