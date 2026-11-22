// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Opc.Ua.Redundancy;

namespace RedundantPubSub
{
    /// <summary>
    /// Hosted service that owns the lifetime of the Raft consensus instance, disposing it on
    /// shutdown so the underlying transport and storage are released cleanly.
    /// </summary>
    public sealed class RaftLifetimeService : IHostedService, IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="RaftLifetimeService"/>.
        /// </summary>
        /// <param name="consensus">The Raft consensus instance to own.</param>
        public RaftLifetimeService(IRaftConsensus consensus)
        {
            m_consensus = consensus ?? throw new ArgumentNullException(nameof(consensus));
        }

        /// <summary>
        /// Starts the service. The consensus instance is already running, so this is a no-op.
        /// </summary>
        /// <param name="cancellationToken">Token used to observe cancellation.</param>
        /// <returns>A completed task.</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops the service and disposes the consensus instance.
        /// </summary>
        /// <param name="cancellationToken">Token used to observe cancellation.</param>
        /// <returns>A task that completes once the consensus instance is disposed.</returns>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes the owned Raft consensus instance exactly once.
        /// </summary>
        /// <returns>A task that completes once disposal finishes.</returns>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                await m_consensus.DisposeAsync().ConfigureAwait(false);
            }
        }

        private readonly IRaftConsensus m_consensus;
        private int m_disposed;
    }
}
