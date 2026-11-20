/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// A task run by the hosted server immediately after the server has
    /// started, with access to the live <see cref="IServerContext"/>. Every
    /// implementation registered in DI is invoked once. This is the seam
    /// features use to wire runtime behavior that needs the fully-initialized
    /// server (a bound address space, the populated message context, the
    /// <c>ServerObject</c>) without subclassing <see cref="StandardServer"/>.
    /// </summary>
    /// <remarks>
    /// A task that needs a particular subsystem takes it as a constructor
    /// dependency, which is how every implementation in this repository already
    /// obtains what it needs. The context carries only what is genuinely ambient.
    /// </remarks>
    public interface IServerStartupTask
    {
        /// <summary>
        /// Invoked once after the server has started.
        /// A failure aborts the hosted server's startup and triggers cleanup.
        /// </summary>
        /// <param name="server">The live server context.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask OnServerStartedAsync(IServerContext server, CancellationToken cancellationToken = default);
    }
}
