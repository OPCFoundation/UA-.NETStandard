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
