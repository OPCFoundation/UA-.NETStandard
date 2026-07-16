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
