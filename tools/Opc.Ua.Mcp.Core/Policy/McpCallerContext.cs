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
using System.Security.Claims;
using System.Threading;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Trusted host identity, never accepted as a tool argument.
    /// Issuer and local authority are part of identity so subjects cannot collide across authorities.
    /// </summary>
    /// <param name="Subject">The authenticated stable subject or explicit local host identity.</param>
    /// <param name="Issuer">The authority that established the subject.</param>
    /// <param name="IsLocal">Whether the identity comes from explicit in-process host authority.</param>
    public sealed record McpCallerIdentity(string Subject, string Issuer, bool IsLocal = false)
    {
        /// <summary>
        /// Validates the host identity before it is used as an ownership key.
        /// </summary>
        public void Validate()
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(Subject);
            ArgumentException.ThrowIfNullOrWhiteSpace(Issuer);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Subject.Length, 4096);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(Issuer.Length, 4096);
        }
    }

    /// <summary>
    /// Carries trusted request identity through asynchronous tool and client calls.
    /// A completed scope cannot authorize work retained in a child execution context.
    /// </summary>
    public sealed class McpCallerContext
    {
        /// <summary>
        /// Gets the active trusted identity, or null when no request scope established one.
        /// </summary>
        public McpCallerIdentity? Current
        {
            get
            {
                Scope? scope = m_scope.Value;
                return scope is not null && scope.Active ? scope.Caller : null;
            }
        }

        /// <summary>
        /// Gets whether a request context, including an unauthenticated one, is present.
        /// It prevents a network request from inheriting the embedding host's local fallback identity.
        /// </summary>
        public bool HasRequestScope => m_scope.Value is not null;

        /// <summary>
        /// Enters an explicit host-established identity scope.
        /// This method is for trusted embedding code, never for data supplied by an MCP caller.
        /// </summary>
        public IDisposable EnterTrustedCaller(McpCallerIdentity caller)
        {
            ArgumentNullException.ThrowIfNull(caller);
            caller.Validate();
            return EnterRequest(caller);
        }

        /// <summary>
        /// Extracts stable authenticated subject/issuer identity without treating a display name as authority.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static McpCallerIdentity? FromPrincipal(ClaimsPrincipal? principal)
        {
            if (principal is null)
            {
                return null;
            }
            McpCallerIdentity? caller = null;
            foreach (ClaimsIdentity authenticated in principal.Identities)
            {
                if (!authenticated.IsAuthenticated)
                {
                    continue;
                }
                foreach (Claim subject in authenticated.Claims)
                {
                    if (subject.Type is not (ClaimTypes.NameIdentifier or "sub") ||
                        string.IsNullOrWhiteSpace(subject.Value) ||
                        string.IsNullOrWhiteSpace(subject.Issuer))
                    {
                        continue;
                    }
                    var identity = new McpCallerIdentity(subject.Value, subject.Issuer);
                    identity.Validate();
                    if (caller is not null && caller != identity)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadUserAccessDenied, "The request has ambiguous authenticated identities.");
                    }
                    caller = identity;
                }
            }
            return caller;
        }

        /// <summary>
        /// Enters a transport request, preserving absence of authentication as an explicit denial context.
        /// </summary>
        internal IDisposable EnterRequest(McpCallerIdentity? caller)
        {
            var scope = new Scope(this, caller, m_scope.Value);
            m_scope.Value = scope;
            return scope;
        }

        /// <summary>The issuer reserved for explicitly configured local host authority.</summary>
        public const string LocalIssuer = "urn:opcfoundation:mcp:local-host";

        private readonly AsyncLocal<Scope?> m_scope = new();

        /// <summary>
        /// Restores the parent request scope and revokes captured copies on disposal.
        /// </summary>
        private sealed class Scope(McpCallerContext owner, McpCallerIdentity? caller, Scope? previous) : IDisposable
        {
            /// <summary>
            /// Gets the identity fixed when this scope was entered.
            /// </summary>
            public McpCallerIdentity? Caller { get; } = caller;

            /// <summary>
            /// Gets whether the owning request is still active.
            /// </summary>
            public bool Active => Volatile.Read(ref m_disposed) == 0;

            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) == 0 &&
                    ReferenceEquals(owner.m_scope.Value, this))
                {
                    owner.m_scope.Value = previous;
                }
            }

            private int m_disposed;
        }
    }
}
