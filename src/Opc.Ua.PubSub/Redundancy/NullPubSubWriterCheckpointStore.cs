/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// No-op <see cref="IPubSubWriterCheckpointStore"/> used when Hot redundancy is not
    /// configured, so the non-redundant publish path keeps its process-local SequenceNumbers
    /// without any shared-store interaction.
    /// </summary>
    public sealed class NullPubSubWriterCheckpointStore : IPubSubWriterCheckpointStore
    {
        /// <summary>
        /// Gets the shared no-op instance.
        /// </summary>
        public static NullPubSubWriterCheckpointStore Instance { get; } = new();

        private NullPubSubWriterCheckpointStore()
        {
        }

        /// <inheritdoc/>
        public ValueTask<uint?> GetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<uint?>((uint?)null);
        }

        /// <inheritdoc/>
        public ValueTask SetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            uint sequenceNumber,
            CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask SetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            uint sequenceNumber,
            long fencingToken,
            CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
