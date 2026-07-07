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
using System.Buffers.Binary;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Redundancy;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Distributed <see cref="IPubSubWriterCheckpointStore"/> backed by an
    /// <see cref="ISharedKeyValueStore"/>, sharing live DataSetWriter SequenceNumbers across a
    /// redundant PubSub set so a promoted Hot standby continues them (Part 14 §9.1.6 / §7.2.5.4.1).
    /// </summary>
    /// <remarks>
    /// SequenceNumbers are a high-water mark rather than a secret, so they are stored without
    /// record protection and may use an eventually-consistent store; the take-over safety margin
    /// applied by the publisher covers any gossip lag.
    /// </remarks>
    public sealed class SharedStorePubSubWriterCheckpointStore : IPubSubWriterCheckpointStore
    {
        /// <summary>
        /// Initializes a new <see cref="SharedStorePubSubWriterCheckpointStore"/>.
        /// </summary>
        /// <param name="store">Shared key/value backend used for checkpoints.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="store"/> is <see langword="null"/>.
        /// </exception>
        public SharedStorePubSubWriterCheckpointStore(ISharedKeyValueStore store)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <inheritdoc/>
        public async ValueTask<uint?> GetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            CancellationToken cancellationToken = default)
        {
            (bool found, ByteString value) = await m_store
                .TryGetAsync(BuildKey(writerGroupComponentId, dataSetWriterId), cancellationToken)
                .ConfigureAwait(false);
            if (!found || value.IsNull)
            {
                return null;
            }

            ReadOnlySpan<byte> bytes = value.Span;
            if (bytes.Length < sizeof(uint))
            {
                return null;
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        /// <inheritdoc/>
        public async ValueTask SetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            uint sequenceNumber,
            CancellationToken cancellationToken = default)
        {
            var buffer = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, sequenceNumber);
            await m_store
                .SetAsync(
                    BuildKey(writerGroupComponentId, dataSetWriterId),
                    new ByteString(buffer),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static string BuildKey(string writerGroupComponentId, ushort dataSetWriterId)
        {
            if (string.IsNullOrEmpty(writerGroupComponentId))
            {
                throw new ArgumentException(
                    "writerGroupComponentId is required.", nameof(writerGroupComponentId));
            }

            return string.Concat(
                PubSubRedundancyStoreKeys.CheckpointPrefix,
                writerGroupComponentId,
                "/",
                dataSetWriterId.ToString(CultureInfo.InvariantCulture));
        }

        private readonly ISharedKeyValueStore m_store;
    }
}
