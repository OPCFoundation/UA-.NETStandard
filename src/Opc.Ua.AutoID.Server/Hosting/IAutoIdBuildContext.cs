// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using Opc.Ua.Di.Server;

namespace Opc.Ua.AutoID.Server.Hosting
{
    /// <summary>
    /// Context handed to every <c>ConfigureAutoIdFor</c> delegate: the AutoID runtime of the
    /// selected Device Integration owner, during server startup.
    /// </summary>
    public interface IAutoIdBuildContext
    {
        /// <summary>
        /// Gets the owner's shared runtime that creates the readers.
        /// </summary>
        AutoIdRuntime Runtime { get; }

        /// <summary>
        /// Gets the node manager that owns the Device Integration address space and the readers.
        /// </summary>
        DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the hosting cancellation token. Honour it: the hosted service uses it to cancel
        /// startup on shutdown.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves a required application service.
        /// </summary>
        /// <typeparam name="T">The service contract.</typeparam>
        T GetRequiredService<T>() where T : notnull;
    }
}
