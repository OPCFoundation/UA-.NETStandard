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

#nullable enable

using System;

namespace Opc.Ua.Client.Rest
{
    /// <summary>
    /// Options for <see cref="RestApiSession"/>.
    /// </summary>
    public sealed class RestApiSessionOptions
    {
        /// <summary>
        /// The user identity used during ActivateSession. Defaults to anonymous.
        /// </summary>
        public IUserIdentity Identity { get; set; } = new UserIdentity();

        /// <summary>
        /// Session display name sent to CreateSession.
        /// </summary>
        public string SessionName { get; set; } = "UrnRestApiSession";

        /// <summary>
        /// Requested session timeout in milliseconds.
        /// </summary>
        public double RequestedSessionTimeout { get; set; } = 60000;

        /// <summary>
        /// Divisor used to calculate the keep-alive interval from the
        /// revised session timeout.
        /// </summary>
        public double KeepAliveDivisor { get; set; } = 4.0;

        /// <summary>
        /// Whether to start the publish loop after activation.
        /// </summary>
        public bool AutoPublish { get; set; }

        /// <summary>
        /// Maximum number of concurrent publish requests.
        /// </summary>
        public int MaxConcurrentPublish { get; set; } = 2;

        /// <summary>
        /// Optional client application configuration for CreateSession
        /// identity fields.
        /// </summary>
        public ApplicationConfiguration? ClientConfiguration { get; set; }

        /// <summary>
        /// Overrides <see cref="CreateSessionRequest.ServerUri"/> sent to the
        /// server. When <c>null</c>, the session uses the client's
        /// <see cref="IRestApiClient.BaseAddress"/>. Set this to the OPC UA
        /// endpoint URL the server advertises (typically of the form
        /// <c>opc.https://host:port/&lt;ApplicationName&gt;</c>) when the
        /// REST endpoint is reachable on a different base URL than the
        /// server's OPC UA discovery URI.
        /// </summary>
        public string? ServerUri { get; set; }

        /// <summary>
        /// Overrides <see cref="CreateSessionRequest.EndpointUrl"/> sent to
        /// the server. Same defaulting rules as <see cref="ServerUri"/>.
        /// </summary>
        public string? EndpointUrl { get; set; }

        /// <summary>
        /// Overrides the <see cref="UserIdentityToken.PolicyId"/> sent on
        /// <see cref="ActivateSessionRequest.UserIdentityToken"/>. When
        /// <c>null</c>, the session wrapper picks the first matching
        /// <see cref="UserTokenPolicy"/> from
        /// <see cref="CreateSessionResponse.ServerEndpoints"/> whose
        /// <see cref="UserTokenPolicy.TokenType"/> matches the configured
        /// <see cref="Identity"/>.
        /// </summary>
        public string? IdentityPolicyId { get; set; }

        /// <summary>
        /// Optional time provider for deterministic keep-alive tests.
        /// </summary>
        public TimeProvider? TimeProvider { get; set; }
    }
}
