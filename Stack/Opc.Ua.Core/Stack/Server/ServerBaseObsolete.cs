/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

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
