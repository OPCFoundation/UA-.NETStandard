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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Di.Server.SoftwareUpdate
{
    /// <summary>
    /// In-memory <see cref="ISoftwareFolder"/> implementation suitable
    /// for unit tests and small fixtures. Holds payloads as
    /// <see cref="byte"/>[] in a
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by the
    /// version identifier.
    /// </summary>
    public sealed class MemorySoftwareFolder : ISoftwareFolder
    {
        /// <summary>
        /// Creates a new in-memory folder bound to
        /// <paramref name="elementId"/>.
        /// </summary>
        public MemorySoftwareFolder(NodeId elementId)
        {
            if (elementId.IsNull) { throw new ArgumentNullException(nameof(elementId)); }
            ElementId = elementId;
        }

        /// <inheritdoc/>
        public NodeId ElementId { get; }

        /// <inheritdoc/>
        public async IAsyncEnumerable<SoftwarePackage> ListVersionsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            foreach (KeyValuePair<string, VersionRecord> kvp in m_versions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return kvp.Value.Metadata;
            }
        }

        /// <inheritdoc/>
        public ValueTask<SoftwarePackage?> GetCurrentVersionAsync(
            CancellationToken cancellationToken = default)
        {
            string? current = m_currentVersion;
            if (current == null)
            {
                return new ValueTask<SoftwarePackage?>((SoftwarePackage?)null);
            }
            return new ValueTask<SoftwarePackage?>(
                m_versions.TryGetValue(current, out VersionRecord? r) ? r.Metadata : null);
        }

        /// <inheritdoc/>
        public ValueTask<SoftwarePackage?> GetVersionAsync(
            string version,
            CancellationToken cancellationToken = default)
        {
            if (version == null) { throw new ArgumentNullException(nameof(version)); }
            return new ValueTask<SoftwarePackage?>(
                m_versions.TryGetValue(version, out VersionRecord? r) ? r.Metadata : null);
        }

        /// <inheritdoc/>
        public ValueTask<Stream> OpenVersionAsync(
            string version,
            CancellationToken cancellationToken = default)
        {
            if (version == null) { throw new ArgumentNullException(nameof(version)); }
            if (!m_versions.TryGetValue(version, out VersionRecord? r))
            {
                throw new FileNotFoundException(
                    $"Software version '{version}' not found.", version);
            }
            Stream stream = new MemoryStream(r.Payload, writable: false);
            return new ValueTask<Stream>(stream);
        }

        /// <inheritdoc/>
        public async ValueTask<SoftwarePackage> AddVersionAsync(
            SoftwarePackage metadata,
            Stream payload,
            CancellationToken cancellationToken = default)
        {
            if (metadata == null) { throw new ArgumentNullException(nameof(metadata)); }
            if (payload == null) { throw new ArgumentNullException(nameof(payload)); }
            if (string.IsNullOrWhiteSpace(metadata.Version))
            {
                throw new ArgumentException(
                    "SoftwarePackage.Version must be non-empty.", nameof(metadata));
            }

            using MemoryStream buffer = new();
            await payload.CopyToAsync(buffer, bufferSize: 81920, cancellationToken)
                .ConfigureAwait(false);
            byte[] bytes = buffer.ToArray();

            SoftwarePackage stamped = metadata with
            {
                SizeBytes = bytes.Length,
                CreatedAt = metadata.CreatedAt == default
                    ? DateTimeOffset.UtcNow
                    : metadata.CreatedAt
            };
            m_versions[stamped.Version] = new VersionRecord(stamped, bytes);
            return stamped;
        }

        /// <inheritdoc/>
        public ValueTask<bool> RemoveVersionAsync(
            string version,
            CancellationToken cancellationToken = default)
        {
            if (version == null) { throw new ArgumentNullException(nameof(version)); }
            bool removed = m_versions.TryRemove(version, out _);
            if (removed && string.Equals(m_currentVersion, version, StringComparison.Ordinal))
            {
                m_currentVersion = null;
            }
            return new ValueTask<bool>(removed);
        }

        /// <inheritdoc/>
        public ValueTask SetCurrentVersionAsync(
            string version,
            CancellationToken cancellationToken = default)
        {
            if (version == null) { throw new ArgumentNullException(nameof(version)); }
            if (!m_versions.ContainsKey(version))
            {
                throw new ArgumentException(
                    $"Cannot mark version '{version}' as current — unknown version.",
                    nameof(version));
            }
            m_currentVersion = version;
            return default;
        }

        private readonly ConcurrentDictionary<string, VersionRecord> m_versions = new();
        private string? m_currentVersion;

        private sealed record VersionRecord(SoftwarePackage Metadata, byte[] Payload);
    }
}
