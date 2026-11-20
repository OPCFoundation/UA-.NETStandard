// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Keyed cache of connected managed sessions.
    /// </summary>
    public interface IManagedSessionPool : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Gets an existing session for <paramref name="key"/> or connects a new one.
        /// </summary>
        Task<ManagedSession> GetOrConnectAsync(
            string key,
            ConfiguredEndpoint endpoint,
            CancellationToken ct = default);

        /// <summary>
        /// Gets an existing session for <paramref name="key"/> or connects a configured new one.
        /// </summary>
        Task<ManagedSession> GetOrConnectAsync(
            string key,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct = default);

        /// <summary>
        /// Removes and closes a cached session.
        /// </summary>
        ValueTask<bool> RemoveAsync(string key, CancellationToken ct = default);
    }
}
