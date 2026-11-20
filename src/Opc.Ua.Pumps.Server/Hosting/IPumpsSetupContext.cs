/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using Opc.Ua.Di.Server.Hosting;

namespace Opc.Ua.Pumps.Server.Hosting
{
    /// <summary>
    /// Context handed to every <c>ConfigurePumps</c> delegate once the
    /// OPC 40223 address space is initialised.
    /// </summary>
    /// <remarks>
    /// A thin typed view over <see cref="IDiPostSetupContext"/>: the Pumps
    /// node manager runs in the Device Integration post-setup pipeline like
    /// every other DI-aware manager, and this only saves the caller the cast.
    /// The full DI context is still reachable through
    /// <see cref="DiContext"/>.
    /// </remarks>
    public interface IPumpsSetupContext
    {
        /// <summary>
        /// Gets the initialised node manager. Its predefined nodes are
        /// populated and its type tree resolved, so a configurator can create
        /// pumps and they are visible immediately.
        /// </summary>
        PumpsNodeManager Manager { get; }

        /// <summary>
        /// Gets the underlying Device Integration post-setup context.
        /// </summary>
        IDiPostSetupContext DiContext { get; }

        /// <summary>
        /// Gets the hosting cancellation token. Honour it: the hosted service
        /// uses it to cancel startup on shutdown.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves a required application service.
        /// </summary>
        /// <typeparam name="T">The service contract.</typeparam>
        T GetRequiredService<T>() where T : notnull;
    }
}
