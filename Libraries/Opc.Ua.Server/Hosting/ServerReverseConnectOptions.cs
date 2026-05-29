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

namespace Opc.Ua.Server.Hosting
{
    /// <summary>
    /// Server-side reverse-connect configuration exposed via the unified
    /// dependency-injection surface. Mirrors the fields of
    /// <see cref="ReverseConnectServerConfiguration"/> with binder-friendly
    /// (settable, default-constructible) properties so that the values can
    /// be loaded directly from an
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    /// section such as <c>OpcUa:Server:ReverseConnect</c>.
    /// </summary>
    /// <remarks>
    /// Wiring is performed automatically by the hosted service when
    /// <see cref="OpcUaServerOptions.ReverseConnect"/> is non-null.
    /// </remarks>
    public sealed class ServerReverseConnectOptions
    {
        /// <summary>
        /// Reverse-connect clients to dial. Each entry produces a
        /// <see cref="ReverseConnectClient"/> in the underlying
        /// <see cref="ServerConfiguration.ReverseConnect"/>.
        /// </summary>
        public IList<ServerReverseConnectClientOptions> Clients { get; }
            = [];

        /// <summary>
        /// Interval in milliseconds between reverse-connect attempts.
        /// Defaults to 15&#160;000&#160;ms.
        /// </summary>
        public int ConnectIntervalMs { get; set; } = 15000;

        /// <summary>
        /// Default per-connection timeout in milliseconds when establishing
        /// a reverse connection. Defaults to 30&#160;000&#160;ms.
        /// </summary>
        public int ConnectTimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Timeout in milliseconds before retrying a reverse connection
        /// that was rejected by the client. Defaults to 60&#160;000&#160;ms.
        /// </summary>
        public int RejectTimeoutMs { get; set; } = 60000;
    }

    /// <summary>
    /// One reverse-connect client entry. Mirrors
    /// <see cref="ReverseConnectClient"/>.
    /// </summary>
    public sealed class ServerReverseConnectClientOptions
    {
        /// <summary>
        /// Client endpoint URL the server should reverse-connect to (e.g.
        /// <c>opc.tcp://client.example.com:4841</c>). Required.
        /// </summary>
        public string? EndpointUrl { get; set; }

        /// <summary>
        /// Optional per-client connection timeout in milliseconds.
        /// Overrides <see cref="ServerReverseConnectOptions.ConnectTimeoutMs"/>
        /// when greater than zero.
        /// </summary>
        public int Timeout { get; set; }

        /// <summary>
        /// Maximum simultaneous reverse-connect sessions for this client.
        /// Zero means unlimited; <c>1</c> permits a single connection at a
        /// time.
        /// </summary>
        public int MaxSessionCount { get; set; }

        /// <summary>
        /// Whether this client entry is active. Defaults to <c>true</c>.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
