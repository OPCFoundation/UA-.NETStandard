/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using Opc.Ua.Di.Server.Hosting;

namespace Opc.Ua.Scales.Server.Hosting
{
    /// <summary>
    /// Context handed to every <c>ConfigureScales</c> delegate once the
    /// OPC 40200 address space is initialised.
    /// </summary>
    /// <remarks>
    /// A thin typed view over <see cref="IDiPostSetupContext"/>: the Scales
    /// node manager runs in the Device Integration post-setup pipeline like
    /// every other DI-aware manager, and this only saves the caller the cast.
    /// </remarks>
    public interface IScalesSetupContext
    {
        /// <summary>
        /// Gets the initialised node manager. Its predefined nodes are
        /// populated and its type tree resolved, so a configurator can create
        /// scales and they are visible immediately.
        /// </summary>
        ScalesNodeManager Manager { get; }

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
