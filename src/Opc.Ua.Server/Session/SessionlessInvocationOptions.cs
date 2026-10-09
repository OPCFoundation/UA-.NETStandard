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
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Enables the built-in Session-less Service invocation of OPC 10000-4
    /// §6.3 and decides which caller identities it accepts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Session-less invocation is limited to the View (except RegisterNodes
    /// and UnregisterNodes), Attribute, Method, NodeManagement and Query
    /// Service Sets. A Server that does not enable it answers a request that
    /// carries no <c>authenticationToken</c> with Bad_ServiceUnsupported.
    /// </para>
    /// <para>
    /// The caller's identity is taken from:
    /// </para>
    /// <list type="number">
    /// <item>
    /// an Access Token in <c>RequestHeader.authenticationToken</c> (§6.3.1),
    /// carried as a String NodeId and validated by the Server's identity
    /// authenticators as a JWT (OPC 10000-6 §7.6.5.2.3). The channel has to
    /// be encrypted (SignAndEncrypt), or HTTPS, so the token is not exposed;
    /// </item>
    /// <item>
    /// otherwise an anonymous user, when <see cref="AllowAnonymous"/> is set
    /// and application authentication through the SecureChannel is
    /// sufficient (§6.3.1).
    /// </item>
    /// </list>
    /// <para>
    /// A <c>ValidateSessionLessRequest</c> handler registered on the session
    /// manager takes precedence over these options.
    /// </para>
    /// <para>
    /// A request without a Session has no Session to account for. Its own
    /// budget, <see cref="MaxConcurrentRequests"/> and
    /// <see cref="MaxConcurrentRequestsPerChannel"/>, bounds how many of
    /// these requests run at the same time, apart from the Sessions of the
    /// Server. A request over the budget is answered with Bad_ServerTooBusy.
    /// </para>
    /// </remarks>
    public sealed class SessionlessInvocationOptions
    {
        /// <summary>
        /// Gets or sets whether an Access Token carried in
        /// <c>RequestHeader.authenticationToken</c> is accepted. Defaults to
        /// <see langword="true"/>.
        /// </summary>
        public bool AcceptAccessTokens { get; set; } = true;

        /// <summary>
        /// Gets or sets whether a request that presents no Access Token runs
        /// as an anonymous user. Defaults to <see langword="false"/>: without
        /// an Access Token the request is rejected with
        /// Bad_IdentityTokenInvalid.
        /// </summary>
        /// <remarks>
        /// Without an Access Token the channel has to authenticate the
        /// application (§6.3.1): on an endpoint that uses security, and on
        /// HTTPS with mutual TLS, the request needs the client certificate of
        /// its channel, otherwise it is rejected with Bad_SecurityChecksFailed,
        /// the status CreateSession uses. The endpoint also has to offer an
        /// anonymous user token policy, otherwise the request is rejected with
        /// Bad_IdentityTokenRejected, as ActivateSession does.
        /// </remarks>
        public bool AllowAnonymous { get; set; }

        /// <summary>
        /// Gets or sets how many Session-less requests the Server runs at
        /// the same time, over all channels. Zero does not limit them.
        /// Defaults to <see cref="DefaultMaxConcurrentRequests"/>.
        /// </summary>
        /// <remarks>
        /// The budget counts a request from the check of its identity until
        /// the service has completed, failed or was cancelled. Requests of
        /// a Session do not count. A request over the limit is answered with
        /// Bad_ServerTooBusy: "The Server does not have the resources to
        /// process the request at this time."
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public int MaxConcurrentRequests
        {
            get => Volatile.Read(ref m_maxConcurrentRequests);
            set => Volatile.Write(ref m_maxConcurrentRequests, ValidateLimit(value));
        }

        /// <summary>
        /// Gets or sets how many Session-less requests run at the same time
        /// on one channel. Zero does not limit them. Defaults to
        /// <see cref="DefaultMaxConcurrentRequestsPerChannel"/>.
        /// </summary>
        /// <remarks>
        /// A channel is a SecureChannel of the opc.tcp binding. The HTTPS
        /// bindings have no SecureChannel per connection, so their requests
        /// are grouped by the network address of the peer instead. Callers
        /// behind one address, for example a proxy, share the limit. The
        /// limit is checked together with <see cref="MaxConcurrentRequests"/>.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        public int MaxConcurrentRequestsPerChannel
        {
            get => Volatile.Read(ref m_maxConcurrentRequestsPerChannel);
            set => Volatile.Write(ref m_maxConcurrentRequestsPerChannel, ValidateLimit(value));
        }

        private static int ValidateLimit(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The limit cannot be negative. Zero does not limit.");
            }
            return value;
        }

        /// <summary>
        /// The default for <see cref="MaxConcurrentRequests"/>.
        /// </summary>
        public const int DefaultMaxConcurrentRequests = 64;

        /// <summary>
        /// The default for <see cref="MaxConcurrentRequestsPerChannel"/>.
        /// </summary>
        public const int DefaultMaxConcurrentRequestsPerChannel = 16;

        private int m_maxConcurrentRequests = DefaultMaxConcurrentRequests;
        private int m_maxConcurrentRequestsPerChannel = DefaultMaxConcurrentRequestsPerChannel;
    }
}
