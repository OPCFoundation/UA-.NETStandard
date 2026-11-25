// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// redundant PubSub set for restoration by a promoted Hot standby (Part 14 §9.1.6 / §7.2.5.4.1).
    /// </summary>
    /// <remarks>
    /// External checkpoints require an enrolled <see cref="FreshnessProtectedKeyValueStore"/>
    /// and the publisher's active fence. A successful fenced write rules out later writes from
    /// older fences but does not recover sequence advances that were never checkpointed.
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
            m_store = FencedSharedStoreValue.RequireStore(store);
        }

        /// <inheritdoc/>
        public async ValueTask<uint?> GetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            CancellationToken cancellationToken = default)
        {
            (bool found, ByteString value) = await FencedSharedStoreValue
                .ReadRetainedAsync(m_store, BuildKey(writerGroupComponentId, dataSetWriterId), cancellationToken)
                .ConfigureAwait(false);
            bool allowUnfenced = m_store is InMemorySharedKeyValueStore;
            if (!found ||
                !FencedSharedStoreValue.TryExtractFixedPayload(value, sizeof(uint), allowUnfenced,
                    out ByteString payload, out _))
            {
                return null;
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(payload.Span);
        }

        /// <inheritdoc/>
        public ValueTask SetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            uint sequenceNumber,
            CancellationToken cancellationToken = default)
        {
            return SetSequenceNumberCoreAsync(
                writerGroupComponentId,
                dataSetWriterId,
                sequenceNumber,
                null,
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask SetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            uint sequenceNumber,
            long fencingToken,
            CancellationToken cancellationToken = default)
        {
            return SetSequenceNumberCoreAsync(
                writerGroupComponentId,
                dataSetWriterId,
                sequenceNumber,
                fencingToken,
                cancellationToken);
        }

        private ValueTask SetSequenceNumberCoreAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            uint sequenceNumber,
            long? fencingToken,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, sequenceNumber);
            return FencedSharedStoreValue.StoreFixedAsync(
                m_store,
                BuildKey(writerGroupComponentId, dataSetWriterId),
                new ByteString(buffer),
                fencingToken,
                cancellationToken);
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
