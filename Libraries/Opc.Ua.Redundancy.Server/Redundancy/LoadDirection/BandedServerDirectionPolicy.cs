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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Default <see cref="IServerDirectionPolicy"/>. Ranks members by a health <c>ServiceLevel</c> tier (Healthy,
    /// Degraded, NoData, Maintenance — optionally sub-banded within Healthy), keeps only the top tier (including the
    /// local Server), then chooses the least-loaded member using the separate load weight, breaking equal-load bands
    /// at random. A stale or unknown peer view fails safe to the local Server.
    /// </summary>
    public sealed class BandedServerDirectionPolicy : IServerDirectionPolicy
    {
        /// <summary>
        /// Creates the policy.
        /// </summary>
        /// <param name="view">The peer direction view.</param>
        /// <param name="options">The load-direction options (band sizes).</param>
        /// <param name="chooseIndex">
        /// Chooses an index in <c>[0, count)</c> for random tie-breaking; must be safe for concurrent use.
        /// </param>
        public BandedServerDirectionPolicy(
            IPeerDirectionView view,
            LoadDirectionOptions options,
            Func<int, int> chooseIndex)
        {
            m_view = view ?? throw new ArgumentNullException(nameof(view));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_chooseIndex = chooseIndex ?? throw new ArgumentNullException(nameof(chooseIndex));
        }

        /// <inheritdoc/>
        public async ValueTask<string?> SelectTargetServerUriAsync(
            string localServerUri,
            byte localServiceLevel,
            byte localLoadWeight,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(localServerUri))
            {
                return null;
            }

            ArrayOf<PeerDirectionRecord> peers;
            try
            {
                peers = await m_view.GetPeersAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fail safe to self when the peer view cannot be read.
                return null;
            }

            int loadBandSize = Math.Max(1, m_options.LoadBandSize);
            int healthSubBandSize = m_options.HealthSubBandSize;

            var members = new List<Member>
            {
                new(localServerUri, HealthRank(localServiceLevel, healthSubBandSize), localLoadWeight / loadBandSize, true)
            };
            int topRank = members[0].HealthRank;

            foreach (PeerDirectionRecord peer in peers)
            {
                if (string.Equals(peer.ServerUri, localServerUri, StringComparison.Ordinal))
                {
                    continue;
                }
                int rank = HealthRank(peer.ServiceLevel, healthSubBandSize);
                int loadBand = peer.LoadKnown ? peer.LoadWeight / loadBandSize : int.MaxValue;
                members.Add(new Member(peer.ServerUri, rank, loadBand, false));
                if (rank > topRank)
                {
                    topRank = rank;
                }
            }

            List<Member> eligible = members.Where(m => m.HealthRank == topRank).ToList();
            int minLoadBand = eligible.Min(m => m.LoadBand);
            List<Member> leastLoaded = eligible.Where(m => m.LoadBand == minLoadBand).ToList();

            Member chosen = leastLoaded.Count == 1
                ? leastLoaded[0]
                : leastLoaded[BoundedIndex(leastLoaded.Count)];

            return chosen.IsSelf ? null : chosen.ServerUri;
        }

        private int BoundedIndex(int count)
        {
            int index = m_chooseIndex(count);
            return index < 0 || index >= count ? 0 : index;
        }

        private static int HealthRank(byte level, int healthSubBandSize)
        {
            if (ServiceLevels.IsHealthy(level))
            {
                int sub = healthSubBandSize > 0
                    ? (level - ServiceLevels.HealthyMinimum) / healthSubBandSize
                    : 0;
                return HealthyBaseRank + sub;
            }
            if (level >= DegradedMinimum)
            {
                return DegradedRank;
            }
            // NoData (1) ranks above Maintenance (0).
            return level;
        }

        private const int HealthyBaseRank = 3;
        private const int DegradedRank = 2;
        private const byte DegradedMinimum = 2;

        private readonly IPeerDirectionView m_view;
        private readonly LoadDirectionOptions m_options;
        private readonly Func<int, int> m_chooseIndex;

        private readonly record struct Member(string ServerUri, int HealthRank, int LoadBand, bool IsSelf);
    }
}
