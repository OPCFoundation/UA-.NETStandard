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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// The state of a <see cref="RaftSharedKeyValueStore"/> can be captured in
    /// a snapshot and a new replica can start from it, so an application can
    /// bound the Raft log: snapshot, persist, compact the log, and on restart
    /// restore the snapshot before the remaining log is applied.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class RaftSharedKeyValueStoreSnapshotTests
    {
        [Test]
        public async Task SnapshotRestoresTheStateIntoANewStoreAsync()
        {
            await using var source = new RaftSharedKeyValueStore();
            await source.SetAsync("a", ByteString.From(new byte[] { 1 }));
            await source.SetAsync("b", ByteString.From(new byte[] { 2, 3 }));
            await source.SetAsync("c", ByteString.From(new byte[] { 4 }));
            await source.DeleteAsync("c");
            await source.SetAsync("empty", ByteString.From(Array.Empty<byte>()));

            ReadOnlyMemory<byte> snapshot = await source.CreateSnapshotAsync();

            await using var restored = new RaftSharedKeyValueStore();
            restored.RestoreSnapshot(snapshot.Span);

            Assert.That(await ReadAllAsync(restored), Is.EquivalentTo(await ReadAllAsync(source)));
            (bool found, _) = await restored.TryGetAsync("c");
            Assert.That(found, Is.False);
        }

        [Test]
        public async Task LogAppliedAfterTheSnapshotBuildsOnItAsync()
        {
            await using var source = new RaftSharedKeyValueStore();
            await source.SetAsync("counter", ByteString.From(new byte[] { 1 }));
            ReadOnlyMemory<byte> snapshot = await source.CreateSnapshotAsync();

            await using var restored = new RaftSharedKeyValueStore();
            restored.RestoreSnapshot(snapshot.Span);
            bool swapped = await restored.CompareAndSwapAsync(
                "counter",
                ByteString.From(new byte[] { 1 }),
                ByteString.From(new byte[] { 2 }));

            (bool found, ByteString value) = await restored.TryGetAsync("counter");
            Assert.Multiple(() =>
            {
                Assert.That(swapped, Is.True);
                Assert.That(found, Is.True);
                Assert.That(value.ToArray(), Is.EqualTo(new byte[] { 2 }));
            });
        }

        [Test]
        public async Task RestoreAfterTheStoreStartedIsRejectedAsync()
        {
            await using var source = new RaftSharedKeyValueStore();
            await source.SetAsync("a", ByteString.From(new byte[] { 1 }));
            ReadOnlyMemory<byte> snapshot = await source.CreateSnapshotAsync();

            Assert.Throws<InvalidOperationException>(() => source.RestoreSnapshot(snapshot.Span));
        }

        [Test]
        public void CorruptSnapshotIsRejected()
        {
            var store = new RaftSharedKeyValueStore();
            try
            {
                Assert.Throws<FormatException>(() => store.RestoreSnapshot(new byte[] { 0xFF, 1, 2 }));
                Assert.Throws<FormatException>(() => store.RestoreSnapshot(ReadOnlySpan<byte>.Empty));
            }
            finally
            {
                store.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        private static async Task<List<string>> ReadAllAsync(RaftSharedKeyValueStore store)
        {
            var entries = new List<string>();
            await foreach (KeyValuePair<string, ByteString> entry in store.ScanAsync(string.Empty))
            {
                entries.Add(entry.Key + "=" + Convert.ToBase64String(entry.Value.ToArray() ?? []));
            }
            return entries;
        }
    }
}
