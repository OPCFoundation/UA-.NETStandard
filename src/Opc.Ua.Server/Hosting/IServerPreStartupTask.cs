// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Initializes a server subsystem after the internal server context exists
    /// and before node managers start or listeners are exposed.
    /// </summary>
    public interface IServerPreStartupTask
    {
        /// <summary>
        /// Initializes the subsystem. An exception aborts server startup.
        /// </summary>
        ValueTask OnServerStartingAsync(
            IServerContext server,
            CancellationToken cancellationToken = default);
    }
}
