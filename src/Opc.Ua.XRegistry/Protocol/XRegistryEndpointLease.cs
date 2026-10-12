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
using System.Threading.Tasks;

namespace Opc.Ua.XRegistry.Protocol
{
    /// <summary>
    /// Selects an upstream endpoint/session for an authenticated host-supplied caller.
    /// Implementations must isolate credentials and caches by the full caller scope.
    /// </summary>
    public interface IXRegistryEndpointResolver
    {
        /// <summary>
        /// Acquires one operation's stable upstream endpoint. The consumer owns the returned lease.
        /// A prepared operation retains the same lease through commit or abort.
        /// </summary>
        ValueTask<IXRegistryEndpointLease> AcquireAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A caller-bound upstream lifetime, including optional live revocation checks.
    /// Release must not destroy a session while its prepared operation is still in flight.
    /// </summary>
    public interface IXRegistryEndpointLease : IAsyncDisposable
    {
        /// <summary>
        /// Gets the exact caller scope used during acquisition.
        /// </summary>
        XRegistryCallContext Context { get; }

        /// <summary>
        /// Gets the stable endpoint selected for this lease.
        /// </summary>
        IXRegistryEndpoint Endpoint { get; }

        /// <summary>
        /// Gets a signal that terminates current requests when credentials or authorization are revoked.
        /// </summary>
        CancellationToken Revoked { get; }

        /// <summary>
        /// Revalidates the lease immediately before dispatch and publication.
        /// Denied or expired credentials must throw UnauthorizedAccessException, never select another endpoint.
        /// </summary>
        ValueTask EnsureCurrentAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Delegate-backed resolver for application-managed sessions, credential profiles and pools.
    /// No endpoint, token or session is cached by this adapter.
    /// </summary>
    public sealed class XRegistryEndpointResolver(
        Func<XRegistryCallContext, CancellationToken, ValueTask<IXRegistryEndpointLease>> acquire)
        : IXRegistryEndpointResolver
    {
        /// <inheritdoc/>
        public ValueTask<IXRegistryEndpointLease> AcquireAsync(
            XRegistryCallContext context, CancellationToken cancellationToken = default)
        {
            context.ThrowIfNull(nameof(context));
            cancellationToken.ThrowIfCancellationRequested();
            return m_acquire(context, cancellationToken);
        }

        /// <summary>
        /// Borrows a fixed operator endpoint while continuing to forward each trusted caller context.
        /// This explicitly does not provide per-caller upstream credentials.
        /// </summary>
        public static IXRegistryEndpointResolver Borrow(IXRegistryEndpoint endpoint)
        {
            endpoint.ThrowIfNull(nameof(endpoint));
            return new XRegistryEndpointResolver((context, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return new ValueTask<IXRegistryEndpointLease>(new XRegistryEndpointLease(endpoint, context));
            });
        }

        private readonly Func<XRegistryCallContext, CancellationToken, ValueTask<IXRegistryEndpointLease>> m_acquire =
            acquire.ThrowIfNull(nameof(acquire));
    }

    /// <summary>
    /// An asynchronously released lease with optional live authorization and revocation.
    /// The supplied release delegate owns cleanup; omission explicitly borrows the endpoint.
    /// </summary>
    public sealed class XRegistryEndpointLease : IXRegistryEndpointLease
    {
        /// <summary>
        /// Binds one endpoint and its release/authorization callbacks to an immutable trusted caller.
        /// </summary>
        public XRegistryEndpointLease(
            IXRegistryEndpoint endpoint, XRegistryCallContext context, Func<ValueTask>? release = null,
            Func<CancellationToken, ValueTask<bool>>? authorize = null, CancellationToken revoked = default)
        {
            Endpoint = endpoint.ThrowIfNull(nameof(endpoint));
            Context = context.ThrowIfNull(nameof(context));
            Revoked = revoked;
            m_release = release;
            m_authorize = authorize;
        }

        /// <inheritdoc/>
        public XRegistryCallContext Context { get; }

        /// <inheritdoc/>
        public IXRegistryEndpoint Endpoint { get; }

        /// <inheritdoc/>
        public CancellationToken Revoked { get; }

        /// <inheritdoc/>
        public async ValueTask EnsureCurrentAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(XRegistryEndpointLease));
            }
            if (Revoked.IsCancellationRequested ||
                (m_authorize is not null && !await m_authorize(cancellationToken).ConfigureAwait(false)))
            {
                throw new UnauthorizedAccessException("The upstream credential lease is no longer authorized.");
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0 && m_release is not null)
            {
                await m_release().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Compares caller identities, session and role membership without treating role order as significant.
        /// </summary>
        public static bool SameScope(XRegistryCallContext first, XRegistryCallContext second)
        {
            first.ThrowIfNull(nameof(first));
            second.ThrowIfNull(nameof(second));
            return first.SessionId == second.SessionId && SameIdentity(first, second);
        }

        /// <summary>
        /// Compares subject, authority, authentication and roles while allowing a reconnect session ID to change.
        /// </summary>
        public static bool SameIdentity(XRegistryCallContext first, XRegistryCallContext second)
        {
            first.ThrowIfNull(nameof(first));
            second.ThrowIfNull(nameof(second));
            if (first.Subject != second.Subject ||
                first.Authority != second.Authority ||
                first.IsAuthenticated != second.IsAuthenticated ||
                first.Roles.Count != second.Roles.Count)
            {
                return false;
            }
            foreach (string role in first.Roles)
            {
                if (!ContainsRole(second.Roles, role))
                {
                    return false;
                }
            }
            foreach (string role in second.Roles)
            {
                if (!ContainsRole(first.Roles, role))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ContainsRole(ArrayOf<string> roles, string role)
        {
            for (int index = 0; index < roles.Count; index++)
            {
                if (roles[index] == role)
                {
                    return true;
                }
            }
            return false;
        }

        private readonly Func<ValueTask>? m_release;
        private readonly Func<CancellationToken, ValueTask<bool>>? m_authorize;
        private int m_disposed;
    }
}
