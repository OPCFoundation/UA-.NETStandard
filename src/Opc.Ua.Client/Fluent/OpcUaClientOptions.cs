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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Top-level options for
    /// <see cref="Microsoft.Extensions.DependencyInjection.OpcUaClientBuilderExtensions.AddClient(IOpcUaBuilder,System.Action{OpcUaClientOptions})"/>.
    /// </summary>
    public sealed class OpcUaClientOptions
    {
        /// <summary>
        /// The application configuration. Required.
        /// </summary>
        public ApplicationConfiguration? Configuration { get; set; }

        /// <summary>
        /// Default <see cref="ManagedSessionOptions"/> used by the
        /// session factory delegate registered with DI.
        /// </summary>
        public ManagedSessionOptions Session { get; set; } = new();

        /// <summary>
        /// Client identity-provider configuration bound from
        /// <c>OpcUa:Client:Identity</c>.
        /// </summary>
        public OpcUaClientIdentityOptions Identity { get; set; } = new();

        /// <summary>
        /// Client-side reverse-connect configuration. When non-null the
        /// DI container registers a singleton
        /// <see cref="ReverseConnectManager"/> together with an internal
        /// hosted service that opens the configured listener endpoints
        /// asynchronously on host start (eager), while
        /// <see cref="ReverseConnectManager.WaitForConnectionAsync"/>
        /// starts it lazily on first use when no host is present. A missing
        /// <see cref="Configuration"/> is surfaced during the async start
        /// rather than at resolution. The values are also written into
        /// <see cref="ClientConfiguration.ReverseConnect"/> on
        /// <see cref="Configuration"/> so the same data is observable
        /// through the application-configuration surface.
        /// </summary>
        public ClientReverseConnectOptions? ReverseConnect { get; set; }
    }
}
