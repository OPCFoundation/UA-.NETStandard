// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    public sealed class OptionsReaderCoverageTests
    {
        [Test]
        public async Task OptionsReaderQueuesDistinctOptionsAndDropsNullsAsync()
        {
            var monitor = OptionsFactory.Create(new ReaderOptions { Value = 1 });
            var reader = new OptionsReader<ReaderOptions>(monitor, 2);
            ReaderOptions first = new() { Value = 2 };
            ReaderOptions second = new() { Value = 3 };

            monitor.CurrentValue = first;
            monitor.CurrentValue = first;
            monitor.CurrentValue = null!;
            monitor.CurrentValue = second;

            Assert.That(await reader.WaitAsync(CancellationToken.None).ConfigureAwait(false), Is.True);
            Assert.That(reader.TryGet(out ReaderOptions? firstChange), Is.True);
            Assert.That(firstChange?.Value, Is.EqualTo(2));
            Assert.That(reader.TryGet(out ReaderOptions? secondChange), Is.True);
            Assert.That(secondChange?.Value, Is.EqualTo(3));
            Assert.That(reader.TryGet(out _), Is.False);
        }

        [Test]
        public async Task ConvertingOptionsReaderQueuesOnlyNonNullChangesAsync()
        {
            var monitor = OptionsFactory.Create(new ReaderOptions { Value = 1 });
            var reader = new OptionsReader<int, ReaderOptions>(
                monitor,
                option => option.Value > 1 ? option.Value : null);

            monitor.CurrentValue = new ReaderOptions { Value = 1 };
            monitor.CurrentValue = new ReaderOptions { Value = 4 };

            Assert.That(await reader.WaitAsync(CancellationToken.None).ConfigureAwait(false), Is.True);
            Assert.That(reader.TryGetNextChange(out int change), Is.True);
            Assert.That(change, Is.EqualTo(4));
            Assert.That(reader.TryGetNextChange(out _), Is.False);
        }

        private sealed class ReaderOptions
        {
            public int Value { get; set; }
        }
    }
}
