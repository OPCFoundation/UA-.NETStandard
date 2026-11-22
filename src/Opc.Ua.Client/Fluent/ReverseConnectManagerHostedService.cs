// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Drives eager startup and graceful shutdown of the singleton
    /// <see cref="ReverseConnectManager"/> when the application uses the
    /// .NET Generic Host. <see cref="StartAsync"/> ensures the manager's
    /// configured listeners are opened on host start;
    /// <see cref="StopAsync"/> stops the service on host shutdown. When no
    /// host is present the manager still starts lazily on first use via
    /// <see cref="ReverseConnectManager.EnsureStartedAsync"/>.
    /// </summary>
    internal sealed class ReverseConnectManagerHostedService : IHostedService
    {
        /// <summary>
        /// Initializes the hosted service with the managed reverse-connect
        /// manager.
        /// </summary>
        /// <param name="manager">The reverse-connect manager singleton.</param>
        public ReverseConnectManagerHostedService(ReverseConnectManager manager)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Open a hosted startup scope for the whole StartAsync so a
            // CancelPendingStart triggered by this call's cancellation token
            // latches against a genuinely active hosted startup generation. The
            // scope is closed in the finally, after which a late/stale
            // cancellation can no longer latch and poison a later start.
            long generation = m_manager.BeginHostedStartup();
            try
            {
                // Cancelling the host's startup token must abort the underlying
                // reverse-connect startup, not merely this awaiter, so link the
                // token to the manager's pending start.
                using CancellationTokenRegistration registration =
                    cancellationToken.Register(
                        static state => ((ReverseConnectManager)state!).CancelPendingStart(),
                        m_manager);
                try
                {
                    await m_manager.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The awaiter's cancellation can resume inline and dispose the
                    // registration above before it fires (a LIFO/inline-continuation
                    // race), so the pending start would otherwise be left running.
                    // Abort it here too; CancelPendingStart is idempotent, so a
                    // double invocation with the registration is harmless.
                    m_manager.CancelPendingStart();
                    throw;
                }
            }
            finally
            {
                m_manager.EndHostedStartup(generation);
            }
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return m_manager.StopServiceAsync(cancellationToken);
        }

        private readonly ReverseConnectManager m_manager;
    }
}
