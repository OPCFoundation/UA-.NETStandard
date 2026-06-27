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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: in-process, thread-safe <see cref="ISharedKeyValueStore"/>. A single
    /// instance shared by multiple node managers (or multiple in-process
    /// server instances) provides a deterministic backend for both
    /// active/active and active/passive testing and single-process
    /// deployments. Redis / external stores are drop-in replacements of the
    /// same contract.
    /// </summary>
    public sealed class InMemorySharedKeyValueStore : ISharedKeyValueStore, IDisposable
    {
        /// <summary>
        /// Reads the value stored under <paramref name="key"/>.
        /// </summary>
        /// <inheritdoc/>
        public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            lock (m_lock)
            {
                if (m_data.TryGetValue(key, out ByteString value))
                {
                    return new ValueTask<(bool, ByteString)>((true, value));
                }
            }
            return new ValueTask<(bool, ByteString)>((false, default));
        }

        /// <inheritdoc/>
        public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            lock (m_lock)
            {
                m_data[key] = value;
                PublishLocked(new KeyValueChange { Kind = KeyValueChangeKind.Set, Key = key, Value = value });
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<bool> CompareAndSwapAsync(
            string key,
            ByteString expected,
            ByteString value,
            CancellationToken ct = default)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            lock (m_lock)
            {
                bool present = m_data.TryGetValue(key, out ByteString current);
                bool matches = expected.IsNull ? !present : present && current.Equals(expected);
                if (!matches)
                {
                    return new ValueTask<bool>(false);
                }

                m_data[key] = value;
                PublishLocked(new KeyValueChange { Kind = KeyValueChangeKind.Set, Key = key, Value = value });
                return new ValueTask<bool>(true);
            }
        }

        /// <inheritdoc/>
        public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            lock (m_lock)
            {
                if (m_data.Remove(key))
                {
                    PublishLocked(new KeyValueChange { Kind = KeyValueChangeKind.Delete, Key = key });
                    return new ValueTask<bool>(true);
                }
            }
            return new ValueTask<bool>(false);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
            string keyPrefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            keyPrefix ??= string.Empty;

            List<KeyValuePair<string, ByteString>> snapshot;
            lock (m_lock)
            {
                snapshot = new List<KeyValuePair<string, ByteString>>(m_data.Count);
                foreach (KeyValuePair<string, ByteString> entry in m_data)
                {
                    if (entry.Key.StartsWith(keyPrefix, StringComparison.Ordinal))
                    {
                        snapshot.Add(entry);
                    }
                }
            }

            foreach (KeyValuePair<string, ByteString> entry in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                yield return entry;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValueChange> WatchAsync(
            string keyPrefix,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var watcher = new Watcher(keyPrefix ?? string.Empty);
            lock (m_lock)
            {
                m_watchers.Add(watcher);
            }

            try
            {
                await foreach (KeyValueChange change in watcher.Channel.Reader
                    .ReadAllAsync(ct)
                    .ConfigureAwait(false))
                {
                    yield return change;
                }
            }
            finally
            {
                lock (m_lock)
                {
                    m_watchers.Remove(watcher);
                }
                watcher.Channel.Writer.TryComplete();
            }
        }

        /// <summary>
        /// Completes all outstanding watchers.
        /// </summary>
        public void Dispose()
        {
            lock (m_lock)
            {
                foreach (Watcher watcher in m_watchers)
                {
                    watcher.Channel.Writer.TryComplete();
                }
                m_watchers.Clear();
                m_data.Clear();
            }
        }

        private void PublishLocked(KeyValueChange change)
        {
            for (int ii = 0; ii < m_watchers.Count; ii++)
            {
                Watcher watcher = m_watchers[ii];
                if (change.Key.StartsWith(watcher.Prefix, StringComparison.Ordinal))
                {
                    watcher.Channel.Writer.TryWrite(change);
                }
            }
        }

        private sealed class Watcher
        {
            public Watcher(string prefix)
            {
                Prefix = prefix;
            }

            public string Prefix { get; }

            public Channel<KeyValueChange> Channel { get; } =
                System.Threading.Channels.Channel.CreateUnbounded<KeyValueChange>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, ByteString> m_data = new(StringComparer.Ordinal);
        private readonly List<Watcher> m_watchers = [];
    }
}
