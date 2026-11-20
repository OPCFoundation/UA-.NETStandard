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
    /// Creates managed sessions for caller-supplied endpoints from DI options.
    /// </summary>
    public interface IManagedSessionFactory
    {
        /// <summary>
        /// Connects a managed session to <paramref name="endpoint"/>.
        /// </summary>
        Task<ManagedSession> ConnectAsync(
            ConfiguredEndpoint endpoint,
            CancellationToken ct = default);

        /// <summary>
        /// Connects a managed session to <paramref name="endpoint"/> after
        /// applying additional builder customization.
        /// </summary>
        Task<ManagedSession> ConnectAsync(
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct = default);

        /// <summary>
        /// Connects a managed session to <paramref name="endpoint"/> using reverse connect.
        /// </summary>
        Task<ManagedSession> ConnectReverseAsync(
            ReverseConnectManager manager,
            Uri serverUri,
            ConfiguredEndpoint endpoint,
            CancellationToken ct = default);

        /// <summary>
        /// Connects a managed session to <paramref name="endpoint"/> using reverse connect.
        /// </summary>
        Task<ManagedSession> ConnectReverseAsync(
            ReverseConnectManager manager,
            Uri serverUri,
            ConfiguredEndpoint endpoint,
            Action<ManagedSessionBuilder> configure,
            CancellationToken ct = default);
    }
}
