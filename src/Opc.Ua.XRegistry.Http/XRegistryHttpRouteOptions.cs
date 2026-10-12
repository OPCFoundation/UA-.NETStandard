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

#if NET8_0_OR_GREATER
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    /// <summary>
    /// Public-root, inbound identity and authorization policy for one HTTP route mount.
    /// Upstream operator credentials never authenticate an inbound caller.
    /// </summary>
    public sealed record XRegistryHttpRouteOptions
    {
        /// <summary>
        /// Initializes route policy with an explicitly configured public registry root.
        /// The root and transport limits are validated when the route is mapped.
        /// </summary>
        /// <param name="publicRoot">
        /// The absolute public root used to publish registry URLs, never derived from request Host
        /// or forwarding headers.
        /// HTTPS is required unless <see cref="Transport"/> permits loopback HTTP.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="publicRoot"/> is <see langword="null"/>.
        /// </exception>
        public XRegistryHttpRouteOptions(Uri publicRoot)
        {
            publicRoot.ThrowIfNull(nameof(publicRoot));
            PublicRoot = publicRoot;
        }

        /// <summary>
        /// Gets the configured origin and registry root used to render public registry links.
        /// Request Host and forwarding headers cannot override this value.
        /// </summary>
        public Uri PublicRoot { get; }

        /// <summary>
        /// Gets the transport limits, public-root policy and telemetry for this mount.
        /// Defaults to a new <see cref="XRegistryHttpOptions"/> instance and is validated when the route is mapped.
        /// This value must not be <see langword="null"/>.
        /// </summary>
        public XRegistryHttpOptions Transport { get; init; } = new();

        /// <summary>
        /// Gets whether the inbound call context must report an authenticated user before authorization or inspection.
        /// The default is <see langword="true"/>. Upstream client credentials do not satisfy this check.
        /// </summary>
        /// <remarks>
        /// When disabled, anonymous calls still pass through <see cref="AuthorizeAsync"/>;
        /// writes still require an explicit authorization policy.
        /// </remarks>
        public bool RequireAuthenticatedUser { get; init; } = true;

        /// <summary>
        /// Creates a context from a trusted inbound identity provider. When absent,
        /// the authenticated HttpContext.User principal supplies subject, roles and authority.
        /// This delegate must never accept an unverified identity header.
        /// </summary>
        /// <value>
        /// An optional delegate that receives the HTTP context and operation cancellation token,
        /// and returns a non-null caller context.
        /// </value>
        public Func<HttpContext, CancellationToken, ValueTask<XRegistryCallContext>>? CreateContextAsync { get; init; }

        /// <summary>
        /// Authorizes the action, canonical path, view, parameters and explicit context
        /// before inspection or body processing. The request has no payload at this stage.
        /// When absent, only reads and OPTIONS are allowed; writes are denied.
        /// May also be called to filter advertised methods.
        /// </summary>
        /// <value>
        /// An optional delegate that receives the HTTP context, payload-free protocol request and cancellation token,
        /// and returns <see langword="true"/> when the action is allowed.
        /// </value>
        public Func<HttpContext, XRegistryRequest, CancellationToken, ValueTask<bool>>? AuthorizeAsync { get; init; }
    }
}
#endif
