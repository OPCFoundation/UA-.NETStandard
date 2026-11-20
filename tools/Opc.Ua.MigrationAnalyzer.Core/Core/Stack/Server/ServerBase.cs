/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// A class for obsolete methods of <see cref="ServerBase"/>.
    /// </summary>
    public static class ServerBaseObsolete
    {
        /// <summary>
        /// Starts the server.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The object that stores the configurable configuration information
        /// for a UA application</param>
        /// <param name="baseAddresses">The array of Uri elements which contains base addresses.</param>
        /// <returns>Returns a host for a UA service.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use StartAsync")]
        public static ServiceHost Start(this IServerBase server, ApplicationConfiguration configuration, params Uri[] baseAddresses)
        {
            return server.StartAsync(configuration, default, baseAddresses).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Starts the server (called from a dedicated host process).
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="configuration">The object that stores the configurable configuration
        /// information for a UA application.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
        [Obsolete("Use StartAsync")]
        public static void Start(this IServerBase server, ApplicationConfiguration configuration)
        {
            server.StartAsync(configuration).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Stops the server and releases all resources.
        /// </summary>
        [Obsolete("Use StopAsync")]
        public static void Stop(this IServerBase server)
        {
            server.StopAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
