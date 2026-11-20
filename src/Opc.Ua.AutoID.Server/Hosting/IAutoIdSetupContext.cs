// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using Opc.Ua.Di.Server.Hosting;

namespace Opc.Ua.AutoID.Server.Hosting
{
    /// <summary>
    /// Context handed to every <c>ConfigureAutoId</c> delegate once the OPC 30010 address space
    /// is initialised.
    /// </summary>
    /// <remarks>
    /// A thin typed view over <see cref="IDiPostSetupContext"/>: the AutoID node manager runs in
    /// the Device Integration post-setup pipeline like every other DI-aware manager. The full DI
    /// context stays reachable through <see cref="DiContext"/>.
    /// </remarks>
    public interface IAutoIdSetupContext
    {
        /// <summary>
        /// Gets the initialised node manager.
        /// </summary>
        AutoIdNodeManager Manager { get; }

        /// <summary>
        /// Gets the runtime that creates the readers.
        /// </summary>
        AutoIdRuntime Runtime { get; }

        /// <summary>
        /// Gets the underlying Device Integration post-setup context.
        /// </summary>
        IDiPostSetupContext DiContext { get; }

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
