/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Opc.Ua.WotCon.Samples.Tests
{
    internal static class TestPorts
    {
        public static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
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

        public static int[] GetFreePorts(int count)
        {
            var ports = new HashSet<int>();
            while (ports.Count < count)
            {
                ports.Add(GetFreePort());
            }
            return [.. ports];
        }
    }
}
