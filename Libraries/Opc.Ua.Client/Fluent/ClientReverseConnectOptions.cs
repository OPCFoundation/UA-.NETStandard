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

using System.Collections.Generic;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Client-side reverse-connect configuration exposed via the unified
    /// dependency-injection surface. Mirrors the fields of
    /// <see cref="ReverseConnectClientConfiguration"/> with binder-friendly
    /// (settable, default-constructible) properties so the values can be
    /// loaded directly from an
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// section such as <c>OpcUa:Client:ReverseConnect</c>.
    /// </summary>
    /// <remarks>
    /// When <see cref="OpcUaClientOptions.ReverseConnect"/> is set, the DI
    /// container registers a singleton
    /// <see cref="ReverseConnectManager"/> that opens the configured
    /// listener endpoints on first resolution. Consumers awaiting an
    /// inbound reverse-hello message resolve the manager and call
    /// <see cref="ReverseConnectManager.WaitForConnectionAsync"/>.
    /// </remarks>
    public sealed class ClientReverseConnectOptions
    {
        /// <summary>
        /// Local listener endpoints (e.g. <c>opc.tcp://0.0.0.0:4841</c>)
        /// the manager should bind to. Each URL is registered via
        /// <see cref="ReverseConnectManager.AddEndpoint"/>.
        /// </summary>
        public IList<string> ClientEndpointUrls { get; } = [];

        /// <summary>
        /// Time in milliseconds a reverse-hello port is held open while
        /// waiting for a server callback before the request is rejected.
        /// Defaults to 15&#160;000&#160;ms.
        /// </summary>
        public int HoldTimeMs { get; set; } = 15000;

        /// <summary>
        /// Maximum time in milliseconds to wait for a reverse-hello message
        /// before <see cref="ReverseConnectManager.WaitForConnectionAsync"/>
        /// times out. Defaults to 20&#160;000&#160;ms.
        /// </summary>
        public int WaitTimeoutMs { get; set; } = 20000;
    }
}
