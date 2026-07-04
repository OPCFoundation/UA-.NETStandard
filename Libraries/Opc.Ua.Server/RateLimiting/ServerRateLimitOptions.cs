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

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Deterministic, configurable admission-control limits for the server's
    /// transport connections and session establishment.
    /// </summary>
    /// <remarks>
    /// These options drive the default
    /// <see cref="IServerRateLimiterProvider"/> implementation, which builds
    /// <see cref="System.Threading.RateLimiting.RateLimiter"/> instances from
    /// them. Rate limiting is ON by default with conservative limits sized so
    /// normal and bulk-but-well-behaved load (including staged bulk connects) is
    /// unaffected; only a connect storm is shed. Tune or disable per deployment
    /// via the DI options surface, or replace the whole provider via DI.
    /// </remarks>
    public sealed class ServerRateLimitOptions
    {
        /// <summary>
        /// The default maximum number of connections admitted per second, per
        /// remote address (sustained token-bucket replenishment rate).
        /// </summary>
        public const int DefaultConnectionsPerSecond = 500;

        /// <summary>
        /// The default connection burst (token-bucket capacity), per remote address.
        /// </summary>
        public const int DefaultConnectionBurst = 1000;

        /// <summary>
        /// The default listener socket backlog.
        /// </summary>
        public const int DefaultListenBacklog = 512;

        /// <summary>
        /// Gets or sets a value indicating whether any server rate limiting is
        /// applied. When <c>false</c>, no connection or session admission limits
        /// are enforced and the listener backlog default is used.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the listener socket's pending-connection backlog. A larger
        /// backlog absorbs a burst of simultaneous connects rather than letting the
        /// OS drop them before the accept loop can service them.
        /// </summary>
        public int ListenBacklog { get; set; } = DefaultListenBacklog;

        /// <summary>
        /// Gets or sets a value indicating whether inbound connections are rate
        /// limited (server-wide) before a secure channel is created.
        /// </summary>
        public bool ConnectionRateLimitEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the sustained inbound-connection admission rate per second,
        /// server-wide (a single bucket shared across all remote peers).
        /// </summary>
        public int ConnectionsPerSecond { get; set; } = DefaultConnectionsPerSecond;

        /// <summary>
        /// Gets or sets the inbound-connection burst capacity, server-wide.
        /// </summary>
        public int ConnectionBurst { get; set; } = DefaultConnectionBurst;

        /// <summary>
        /// Gets or sets a value indicating whether session establishment
        /// (<c>CreateSession</c> / <c>ActivateSession</c>) is admission limited.
        /// </summary>
        public bool SessionRateLimitEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of concurrent in-flight session
        /// establishment operations. Bounds the CPU-bound handshake work so a
        /// connect storm cannot saturate every core and starve steady-state
        /// publish delivery. Zero or negative selects
        /// <see cref="DefaultMaxConcurrentSessionEstablishment"/>.
        /// </summary>
        public int MaxConcurrentSessionEstablishment { get; set; }
            = DefaultMaxConcurrentSessionEstablishment;

        /// <summary>
        /// Gets or sets the number of session establishment operations that may
        /// wait for a permit before further operations are rejected with
        /// <c>BadServerTooBusy</c>. The default (0) rejects immediately for a
        /// deterministic, fast "busy" signal.
        /// </summary>
        public int SessionEstablishmentQueueLimit { get; set; }

        /// <summary>
        /// The default maximum number of concurrent in-flight session
        /// establishment operations, scaled with the processor count and floored
        /// generously so legitimate load is never rejected.
        /// </summary>
        public static int DefaultMaxConcurrentSessionEstablishment
            => Math.Max(Environment.ProcessorCount * 8, 128);
    }
}
