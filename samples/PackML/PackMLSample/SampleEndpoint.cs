// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Net;

namespace PackMLSample
{
    /// <summary>
    /// Keeps the simulated equipment's server and client on a numeric loopback endpoint.
    /// </summary>
    internal static class SampleEndpoint
    {
        /// <summary>
        /// Validates local endpoints and prevents DNS hosts from selecting a wildcard transport listener.
        /// </summary>
        internal static string Normalize(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? address) || address.Scheme != "opc.tcp" ||
                !address.IsLoopback)
            {
                throw new ArgumentException("The simulated sample requires an absolute loopback opc.tcp endpoint.");
            }
            return address.HostNameType == UriHostNameType.Dns
                ? new UriBuilder(address) { Host = IPAddress.Loopback.ToString() }.Uri.AbsoluteUri
                : address.AbsoluteUri;
        }
    }
}
