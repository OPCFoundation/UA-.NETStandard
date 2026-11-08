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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// An explicitly selected, process-local store with the same snapshot isolation and format limits
    /// as <see cref="FileSystemLadsStore"/>. It is not a fallback for persistence failures.
    /// </summary>
    public sealed class InMemoryLadsStore : ILadsStore
    {
        /// <summary>
        /// Creates an empty store with a byte limit for each complete serialized snapshot.
        /// </summary>
        public InMemoryLadsStore(int maxBytes = 64 * 1024 * 1024)
        {
            LadsSnapshotCodec.ValidateLimit(maxBytes);
            m_maxBytes = maxBytes;
        }

        /// <inheritdoc/>
        public ValueTask<LadsStoredDeviceData?> LoadAsync(
            string deviceIdentity,
            CancellationToken cancellationToken = default)
        {
            LadsSnapshotCodec.ValidateIdentity(deviceIdentity);
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? content;
            lock (m_lock)
            {
                m_snapshots.TryGetValue(deviceIdentity, out content);
            }
            LadsStoredDeviceData? data = content == null
                ? null
                : LadsSnapshotCodec.Decode(content, deviceIdentity, m_maxBytes);
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<LadsStoredDeviceData?>(data);
        }

        /// <inheritdoc/>
        public ValueTask SaveAsync(
            string deviceIdentity,
            LadsStoredDeviceData data,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] content = LadsSnapshotCodec.Encode(data, deviceIdentity, m_maxBytes);
            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_snapshots[deviceIdentity] = content;
            }
            return default;
        }

        /// <inheritdoc/>
        public ValueTask DeleteAsync(string deviceIdentity, CancellationToken cancellationToken = default)
        {
            LadsSnapshotCodec.ValidateIdentity(deviceIdentity);
            lock (m_lock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_snapshots.Remove(deviceIdentity);
            }
            return default;
        }

        /// <summary>The complete snapshot byte limit.</summary>
        private readonly int m_maxBytes;

        /// <summary>Guards the snapshot map, never exposed to consumers.</summary>
        private readonly Lock m_lock = new();

        /// <summary>Private encoded snapshots prevent callers from mutating saved or loaded state.</summary>
        private readonly Dictionary<string, byte[]> m_snapshots = new(System.StringComparer.Ordinal);
    }
}
