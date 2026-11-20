/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Optional capture of an authenticated session's dispatch binding.
    /// </summary>
    /// <remarks>
    /// The returned client retains its transport generation, session incarnation
    /// and namespace/server mapping. Requests never reconnect or retry onto a
    /// replacement binding. Invalidated bindings fail with BadSecurityChecksFailed.
    /// Closing or disposing the returned client does not close its owning session.
    /// </remarks>
    public interface ISessionBindingProvider
    {
        /// <summary>
        /// Captures a client for the current authenticated binding.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The session is unavailable, changed during capture or has no supported
        /// generation-bound transport. Unsupported transports report BadNotSupported.
        /// </exception>
        /// <param name="ct">The cancellation token supplied by the caller.</param>
        ValueTask<ISessionClient> CreateBindingAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// A captured session client that exposes the validity of its retained
    /// transport, authenticated session and namespace mapping.
    /// </summary>
    public interface ISessionBinding : ISessionClient
    {
        /// <summary>
        /// Gets whether the captured binding is still current. This is an
        /// observation, not a dispatch lease: each request also validates the
        /// same binding when it is sent.
        /// </summary>
        bool IsCurrent { get; }
    }
}
