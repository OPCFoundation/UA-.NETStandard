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

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// An <see cref="IServiceLevelProvider"/> driven by leadership: the
    /// leader reports the highest level so clients prefer it; standbys report
    /// a lower level. This is how active/passive failover is advertised
    /// through the documented OPC UA <c>ServiceLevel</c> mechanism.
    /// </summary>
    public sealed class LeaderServiceLevelProvider : IServiceLevelProvider, IDisposable
    {
        /// <summary>
        /// Creates a leadership-driven service-level provider.
        /// </summary>
        /// <param name="election">The leader election to follow.</param>
        /// <param name="leaderLevel">Level reported while leader (default 255).</param>
        /// <param name="standbyLevel">Level reported while standby (default 1).</param>
        public LeaderServiceLevelProvider(
            ILeaderElection election,
            byte leaderLevel = 255,
            byte standbyLevel = 1)
        {
            m_election = election ?? throw new ArgumentNullException(nameof(election));
            m_leaderLevel = leaderLevel;
            m_standbyLevel = standbyLevel;
            m_election.LeadershipChanged += OnLeadershipChanged;
        }

        /// <inheritdoc/>
        public event Action<byte>? ServiceLevelChanged;

        /// <inheritdoc/>
        public byte GetServiceLevel()
        {
            return m_election.IsLeader ? m_leaderLevel : m_standbyLevel;
        }

        /// <summary>
        /// Stops following the leader election.
        /// </summary>
        public void Dispose()
        {
            m_election.LeadershipChanged -= OnLeadershipChanged;
        }

        private void OnLeadershipChanged(bool isLeader)
        {
            ServiceLevelChanged?.Invoke(isLeader ? m_leaderLevel : m_standbyLevel);
        }

        private readonly ILeaderElection m_election;
        private readonly byte m_leaderLevel;
        private readonly byte m_standbyLevel;
    }
}
