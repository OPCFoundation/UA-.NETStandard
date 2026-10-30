/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    /// <summary>
    /// Injects deterministic qualification and write failures; it is not evidence of actual storage durability.
    /// </summary>
    internal sealed class TestCheckpointStore :
        ISharedKeyValueStore, ISharedKeyValueStoreConsistency, ISharedKeyValueStoreDurability, IDisposable
    {
        internal bool Available { get; set; } = true;
        internal bool Linearizable { get; set; } = true;
        internal int FailWriteAt { get; set; }
        internal bool FailAfterCommit { get; set; }
        internal int WriteAttempts => m_writeAttempts;
        internal ISharedKeyValueStore Raw => m_inner;

        public ValueTask VerifyDurabilityAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Available ? default : throw new IOException("Synthetic unavailable storage.");
        }

        public bool IsLinearizable(string key)
        {
            return Linearizable;
        }

        public bool IsProcessLocal(string key)
        {
            return true;
        }

        public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
        {
            return m_inner.TryGetAsync(key, ct);
        }

        public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
        {
            return m_inner.SetAsync(key, value, ct);
        }

        public async ValueTask<bool> CompareAndSwapAsync(
            string key, ByteString expected, ByteString value, CancellationToken ct = default)
        {
            int attempt = Interlocked.Increment(ref m_writeAttempts);
            if (attempt == FailWriteAt && !FailAfterCommit)
            {
                throw new IOException("Synthetic write failure.");
            }
            bool changed = await m_inner.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
            if (attempt == FailWriteAt && FailAfterCommit)
            {
                throw new IOException("Synthetic reply loss after storage commit.");
            }
            return changed;
        }

        public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            return m_inner.DeleteAsync(key, ct);
        }

        public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix, CancellationToken ct = default)
        {
            return m_inner.ScanAsync(keyPrefix, ct);
        }

        public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default)
        {
            return m_inner.WatchAsync(keyPrefix, ct);
        }

        public void Dispose()
        {
            m_inner.Dispose();
        }

        private readonly InMemorySharedKeyValueStore m_inner = new();
        private int m_writeAttempts;
    }
}
