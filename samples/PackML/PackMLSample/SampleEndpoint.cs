/* ========================================================================
 * Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

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
