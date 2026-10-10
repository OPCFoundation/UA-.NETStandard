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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.WotCon.Tests.Registry
{
    internal sealed class RecordingLeasedResourceStore : IWotRegistryContentLeaseProvider, IDisposable
    {
        public bool SupportsImmutableContentLeases => true;
        public bool CorruptNextWrite { get; set; }
        public string? NextLeaseFault { get; set; }
        public Action<string>? ReadObserved { get; set; }

        public int ActiveLeaseCount
        {
            get
            {
                lock (m_lock)
                {
                    int count = 0;
                    foreach (int leases in m_leases.Values)
                    {
                        count += leases;
                    }
                    return count;
                }
            }
        }

        public ValueTask<ByteString> ReadAsync(
            string resourceKey, long offset, int count, CancellationToken ct = default)
        {
            RecordRead(resourceKey);
            return m_inner.ReadAsync(resourceKey, offset, count, ct);
        }

        public async ValueTask WriteAsync(
            string resourceKey, long offset, ByteString data, CancellationToken ct = default)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (offset == 0 && !CorruptNextWrite &&
                    await m_inner.GetLengthAsync(resourceKey, ct).ConfigureAwait(false) == data.Length)
                {
                    ByteString current = await ReadAsync(resourceKey, 0, data.Length, ct).ConfigureAwait(false);
                    if (current == data)
                    {
                        return;
                    }
                }
                EnsureUnleased(resourceKey);
                if (CorruptNextWrite)
                {
                    byte[] corrupted = data.Span.ToArray();
                    corrupted[0] ^= 0xff;
                    data = ByteString.From(corrupted);
                    CorruptNextWrite = false;
                }
                await m_inner.WriteAsync(resourceKey, offset, data, ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        public ValueTask<long> GetLengthAsync(string resourceKey, CancellationToken ct = default)
        {
            return m_inner.GetLengthAsync(resourceKey, ct);
        }

        public async ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default)
        {
            await m_gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                EnsureUnleased(resourceKey);
                return await m_inner.DeleteAsync(resourceKey, ct).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        public async ValueTask<IWotRegistryContentLease> AcquireContentLeaseAsync(
            string resourceKey, CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                long length = await m_inner.GetLengthAsync(resourceKey, cancellationToken).ConfigureAwait(false);
                if (length < 0)
                {
                    throw new InvalidDataException("The leased content is absent.");
                }
                ByteString bytes = await ReadAsync(
                    resourceKey, 0, checked((int)length), cancellationToken).ConfigureAwait(false);
                lock (m_lock)
                {
                    m_leases.TryGetValue(resourceKey, out int count);
                    m_leases[resourceKey] = count + 1;
                }
                string? fault = NextLeaseFault;
                NextLeaseFault = null;
                return new ContentLease(
                    fault == "key" ? resourceKey + "-wrong" : resourceKey,
                    bytes,
                    fault == "length" ? bytes.Length + 1 : bytes.Length,
                    fault == "digest",
                    () => RecordRead(resourceKey),
                    () => Release(resourceKey));
            }
            finally
            {
                m_gate.Release();
            }
        }

        public int ReadsFor(string key)
        {
            lock (m_lock)
            {
                return m_reads.TryGetValue(key, out int count) ? count : 0;
            }
        }

        public void ClearReads()
        {
            lock (m_lock)
            {
                m_reads.Clear();
            }
        }

        public void Dispose()
        {
            m_gate.Dispose();
        }

        private void EnsureUnleased(string key)
        {
            lock (m_lock)
            {
                if (m_leases.TryGetValue(key, out int count) && count != 0)
                {
                    throw new IOException("Protected immutable content cannot be changed.");
                }
            }
        }

        private void RecordRead(string key)
        {
            lock (m_lock)
            {
                m_reads.TryGetValue(key, out int count);
                m_reads[key] = count + 1;
            }
            ReadObserved?.Invoke(key);
        }

        private void Release(string key)
        {
            lock (m_lock)
            {
                m_leases[key]--;
            }
        }

        private readonly InMemoryResourceStore m_inner = new();
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private readonly Lock m_lock = new();
        private readonly Dictionary<string, int> m_reads = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> m_leases = new(StringComparer.Ordinal);

        private sealed class ContentLease(
            string resourceKey,
            ByteString content,
            long length,
            bool corrupt,
            Action recordRead,
            Action release) : IWotRegistryContentLease
        {
            public string ResourceKey { get; } = resourceKey;
            public long ContentLength => length;

            public ValueTask<ByteString> ReadAsync(
                long offset, int count, CancellationToken cancellationToken = default)
            {
                if (Volatile.Read(ref m_release) is null)
                {
                    throw new ObjectDisposedException(nameof(ContentLease));
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (offset < 0 || count < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset));
                }
                recordRead();
                int take = (int)Math.Min(count, Math.Max(0, content.Length - offset));
                byte[] bytes = take == 0
                    ? []
                    : content.Span.Slice(checked((int)offset), take).ToArray();
                if (corrupt && bytes.Length > 0)
                {
                    bytes[0] ^= 0xff;
                }
                return new ValueTask<ByteString>(ByteString.From(bytes));
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref m_release, null)?.Invoke();
            }

            private Action? m_release = release;
        }
    }
}
