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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

// CA1861: inline literal arrays here are one-shot test fixtures, not hot-path
//   allocations, so hoisting them to static readonly fields adds no value. Suppressed file-level.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Client.Redundancy.Tests
{
    /// <summary>
    /// Coverage for <see cref="RaftLeaderElection"/>: an <see cref="ILeaderElection"/> whose leadership is driven by
    /// <see cref="IRaftConsensus"/> leadership transitions (election, follower state, failover, and start failure).
    /// </summary>
    [TestFixture]
    [Category("ClientRedundancy")]
    public sealed class RaftLeaderElectionTests
    {
        [Test]
        public async Task SingleNodeElectionBecomesLeaderAsync()
        {
            await using var consensus = new InProcessRaftConsensus();
            await using var election = new RaftLeaderElection(consensus);
            var transitions = new List<bool>();
            election.LeadershipChanged += transitions.Add;

            Assert.That(election.IsLeader, Is.False);

            election.Start();
            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True);
            Assert.That(election.IsLeader, Is.True);
            Assert.That(transitions, Is.EqualTo(new[] { true }));
        }

        [Test]
        public async Task StartIsIdempotentAsync()
        {
            await using var consensus = new InProcessRaftConsensus();
            await using var election = new RaftLeaderElection(consensus);

            election.Start();
            election.Start();
            bool acquired = await election.TryAcquireOrRenewAsync().ConfigureAwait(false);

            Assert.That(acquired, Is.True, "a second Start must not disturb established leadership");
        }

        [Test]
        public async Task FollowerIsNotLeaderAsync()
        {
            var cluster = new InProcessRaftCluster();
            await using InProcessRaftConsensus consensus1 = cluster.CreateNode(1);
            await using InProcessRaftConsensus consensus2 = cluster.CreateNode(2);
            await using var election1 = new RaftLeaderElection(consensus1);
            await using var election2 = new RaftLeaderElection(consensus2);

            await consensus1.StartAsync().ConfigureAwait(false);
            await consensus2.StartAsync().ConfigureAwait(false);

            Assert.That(election1.IsLeader, Is.True);
            Assert.That(election2.IsLeader, Is.False);
        }

        [Test]
        public async Task ElectionFollowsConsensusFailoverAsync()
        {
            var cluster = new InProcessRaftCluster();
            InProcessRaftConsensus consensus1 = cluster.CreateNode(1);
            await using InProcessRaftConsensus consensus2 = cluster.CreateNode(2);
            await using var election2 = new RaftLeaderElection(consensus2);

            var node2Transitions = new List<bool>();
            election2.LeadershipChanged += node2Transitions.Add;

            await consensus1.StartAsync().ConfigureAwait(false);
            await consensus2.StartAsync().ConfigureAwait(false);
            Assert.That(election2.IsLeader, Is.False);

            // The leader leaves; election2 must observe the takeover.
            await consensus1.DisposeAsync().ConfigureAwait(false);

            Assert.That(election2.IsLeader, Is.True, "the surviving replica is elected leader");
            Assert.That(node2Transitions, Is.EqualTo(new[] { true }));
        }

        [Test]
        public void NullConsensusConstructorThrows()
        {
            Assert.That(() => new RaftLeaderElection(null!), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public async Task DisposeIsIdempotentAsync()
        {
            var consensus = new InProcessRaftConsensus();
            var election = new RaftLeaderElection(consensus);

            await election.DisposeAsync().ConfigureAwait(false);
            Assert.That(async () => await election.DisposeAsync().ConfigureAwait(false), Throws.Nothing);
            await consensus.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task StartLogsAndSwallowsStartFailureAsync()
        {
            // The fake's StartAsync throws synchronously, so the fire-and-forget
            // StartConsensusAsync runs to its catch before Start() returns and the
            // failure is logged (not surfaced), per the void ILeaderElection.Start
            // contract.
            await using var consensus = new ThrowingStartConsensus();
            var logger = new RecordingLogger();
            await using var election = new RaftLeaderElection(consensus, logger);

            election.Start();

            Assert.That(logger.ErrorCount, Is.EqualTo(1), "a start failure is logged, not surfaced");
        }

        /// <summary>
        /// A consensus replica whose <see cref="IRaftConsensus.StartAsync"/> always throws synchronously, driving the
        /// election's fire-and-forget start into its logging catch path.
        /// </summary>
        private sealed class ThrowingStartConsensus : IRaftConsensus
        {
            public bool IsLeader => false;

            public event Action<bool>? LeadershipChanged
            {
                add { }
                remove { }
            }

            public ChannelReader<ReadOnlyMemory<byte>> Committed => m_committed.Reader;

            public ValueTask StartAsync(CancellationToken ct = default)
            {
                throw new InvalidOperationException("consensus cannot start");
            }

            public ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask CampaignAsync(CancellationToken ct = default)
            {
                return default;
            }

            public ValueTask DisposeAsync()
            {
                m_committed.Writer.TryComplete();
                return default;
            }

            private readonly Channel<ReadOnlyMemory<byte>> m_committed =
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        }

        /// <summary>
        /// A minimal <see cref="ILogger"/> that counts error-level log entries so the election's swallowed start
        /// failure is observable.
        /// </summary>
        private sealed class RecordingLogger : ILogger
        {
            public int ErrorCount => m_errorCount;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Error)
                {
                    Interlocked.Increment(ref m_errorCount);
                }
            }

            private int m_errorCount;
        }
    }
}
