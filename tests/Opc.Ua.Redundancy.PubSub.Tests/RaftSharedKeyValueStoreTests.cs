// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
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
        public async Task UnsupportedCommittedCommandFencesTheStoreAsync()
        {
            await using var consensus = new LoopbackConsensus();
            await using var store = new RaftSharedKeyValueStore(consensus);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            const string key = "before";
            var before = ByteString.From(1);
            await store.SetAsync(key, before, timeout.Token).ConfigureAwait(false);
            Assert.That(store.IsLinearizable(key), Is.True);

            await using IAsyncEnumerator<KeyValueChange> watch = store.WatchAsync(string.Empty, timeout.Token)
                .GetAsyncEnumerator();
            Task<bool> next = watch.MoveNextAsync().AsTask();
            await consensus.ReplayUnsupportedVersionAsync(timeout.Token).ConfigureAwait(false);

            Assert.That(() => next, Throws.TypeOf<FormatException>());
            Assert.That(store.IsLinearizable(key), Is.False);
            Assert.That(async () => await store.CompareAndSwapAsync(key, before, ByteString.From(2), timeout.Token)
                .ConfigureAwait(false), Throws.InvalidOperationException.With.InnerException.TypeOf<FormatException>());
            Assert.That(async () => await store.TryGetAsync(key, timeout.Token).ConfigureAwait(false),
                Throws.InvalidOperationException.With.InnerException.TypeOf<FormatException>());
            Assert.That(consensus.Proposals, Is.EqualTo(1), "the fenced store does not submit more commands");
        }

        private sealed class LoopbackConsensus : IRaftConsensus
        {
            public bool IsLeader => true;

            public event Action<bool>? LeadershipChanged
            {
                add { }
                remove { }
            }

            public ChannelReader<ReadOnlyMemory<byte>> Committed => m_committed.Reader;

            public int Proposals { get; private set; }

            public ValueTask StartAsync(CancellationToken ct = default)
            {
                return default;
            }

            public async ValueTask ProposeAsync(ReadOnlyMemory<byte> command, CancellationToken ct = default)
            {
                Proposals++;
                m_lastCommittedCommand = command;
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

            public ValueTask ReplayUnsupportedVersionAsync(CancellationToken ct)
            {
                byte[] entry = m_lastCommittedCommand.ToArray();
                entry[0] = 0;
                return m_committed.Writer.WriteAsync(entry, ct);
            }

            private readonly Channel<ReadOnlyMemory<byte>> m_committed =
                Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

            private ReadOnlyMemory<byte> m_lastCommittedCommand;
        }
    }
}
