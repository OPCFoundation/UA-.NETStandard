/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
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
