/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Allocates free loopback TCP ports for the sample processes. Using OS-assigned
    /// ephemeral ports avoids collisions with other processes and with host-reserved or
    /// excluded port ranges (for example the Hyper-V / WSL exclusions on Windows).
    /// </summary>
    internal static class TestPorts
    {
        /// <summary>
        /// Reserves and returns a currently-free loopback TCP port.
        /// </summary>
        /// <returns>A free TCP port number.</returns>
        public static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>
        /// Reserves and returns the requested number of distinct free loopback TCP ports.
        /// </summary>
        /// <param name="count">The number of ports to allocate.</param>
        /// <returns>The allocated distinct ports.</returns>
        public static int[] GetFreePorts(int count)
        {
            var ports = new HashSet<int>();
            while (ports.Count < count)
            {
                ports.Add(GetFreePort());
            }

            int[] result = new int[count];
            ports.CopyTo(result);
            return result;
        }
    }
}
