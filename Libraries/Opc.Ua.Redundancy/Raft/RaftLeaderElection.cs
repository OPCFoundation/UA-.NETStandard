/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an <see cref="ILeaderElection"/> backed by native Raft leadership. Unlike
    /// the lease-CAS based <see cref="SharedStoreLeaseElection"/>, leadership here is decided by the consensus
    /// protocol itself (a single leader per term, no split-brain), and leadership transitions are pushed via
    /// <see cref="IRaftConsensus.LeadershipChanged"/>.
    /// </summary>
    public sealed class RaftLeaderElection : ILeaderElection
    {
        /// <summary>
        /// Creates a Raft-backed leader election over the supplied consensus replica.
        /// </summary>
        /// <param name="consensus">The consensus replica whose leadership drives this election.</param>
        /// <param name="logger">Optional logger.</param>
        public RaftLeaderElection(IRaftConsensus consensus, ILogger? logger = null)
        {
            m_consensus = consensus ?? throw new ArgumentNullException(nameof(consensus));
            m_logger = logger;
            m_consensus.LeadershipChanged += OnLeadershipChanged;
        }

        /// <inheritdoc/>
        public bool IsLeader => m_consensus.IsLeader;

        /// <inheritdoc/>
        public event Action<bool>? LeadershipChanged;

        /// <inheritdoc/>
        public async ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
        {
            await m_consensus.CampaignAsync(ct).ConfigureAwait(false);
            return m_consensus.IsLeader;
        }

        /// <inheritdoc/>
        public void Start()
        {
            // Raft leadership is event-driven (no lease-renew loop). Starting
            // simply ensures the underlying consensus replica is running; the
            // ILeaderElection contract is fire-and-forget (void), so failures
            // are logged rather than surfaced.
            if (Interlocked.Exchange(ref m_started, 1) == 0)
            {
                _ = StartConsensusAsync();
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return default;
            }
            m_consensus.LeadershipChanged -= OnLeadershipChanged;
            return default;
        }

        private async Task StartConsensusAsync()
        {
            try
            {
                await m_consensus.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger?.RaftConsensusReplicaFailedToStart(ex);
            }
        }

        private void OnLeadershipChanged(bool value)
        {
            LeadershipChanged?.Invoke(value);
        }

        private readonly IRaftConsensus m_consensus;
        private readonly ILogger? m_logger;
        private int m_started;
        private int m_disposed;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="RaftLeaderElection"/>.
    /// </summary>
    internal static partial class RaftLeaderElectionLog
    {
        [LoggerMessage(EventId = RedundancyEventIds.RaftLeaderElection + 0, Level = LogLevel.Error,
            Message = "Raft consensus replica failed to start for leader election.")]
        public static partial void RaftConsensusReplicaFailedToStart(this ILogger logger, Exception exception);
    }

}
