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
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy.Tests
{
    /// <summary>
    /// Regression tests for <see cref="RaftSharedKeyValueStore"/>.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [Category("Redundancy")]
    public sealed class RaftSharedKeyValueStoreTests
    {
        [Test]
        public async Task PoisonCommittedCommandDoesNotStopLaterAppliesAsync()
        {
            await using var consensus = new LoopbackConsensus();
            await using var store = new RaftSharedKeyValueStore(
                consensus,
                ownsConsensus: false,
                commitTimeout: TimeSpan.FromSeconds(2));

            await store.SetAsync("before", ByteString.From(new byte[] { 1 })).ConfigureAwait(false);
            byte[] poison = consensus.CloneLastCommittedCommand();
            poison[0] = byte.MaxValue;
            await consensus.InjectCommittedAsync(poison).ConfigureAwait(false);

            await store.SetAsync("after", ByteString.From(new byte[] { 2 })).ConfigureAwait(false);
            (bool found, ByteString value) = await store.TryGetAsync("after").ConfigureAwait(false);

            Assert.That(found, Is.True);
            Assert.That(value.ToArray(), Is.EqualTo(new byte[] { 2 }));
        }

        private sealed class LoopbackConsensus : IRaftConsensus
        {
            public bool IsLeader => true;

            public event Action<bool> LeadershipChanged
            {
                add { }
                remove { }
            }

            public ChannelReader<ReadOnlyMemory<byte>> Committed => m_committed.Reader;

            public ValueTask StartAsync(CancellationToken ct = default)
            {
                return default;
            }

            public async ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
            {
                m_lastCommittedCommand = command.ToArray();
                await m_committed.Writer.WriteAsync(command, ct).ConfigureAwait(false);
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

            public byte[] CloneLastCommittedCommand()
            {
                Assert.That(m_lastCommittedCommand, Is.Not.Null);
                return (byte[])m_lastCommittedCommand.Clone();
            }

            public ValueTask InjectCommittedAsync(byte[] command)
            {
                return new ValueTask(m_committed.Writer.WriteAsync(command).AsTask());
            }

            private readonly Channel<ReadOnlyMemory<byte>> m_committed =
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

            private byte[] m_lastCommittedCommand = [];
        }
    }
}
