/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Shares the live per-DataSetWriter message SequenceNumber across a redundant PubSub
    /// set for checkpoint-based restoration by a promoted Hot standby, per OPC UA
    /// Part 14 §9.1.6 (Hot redundancy) and §7.2.5.4.1 (SequenceNumber handling).
    /// </summary>
    /// <remarks>
    /// The active publisher periodically checkpoints its writers' SequenceNumbers. On
    /// take-over the default publisher adds a fixed offset of 1,000 to the stored checkpoint
    /// and advances the local counter only if the calculated value is higher. Checkpoint age,
    /// publication rate, store lag and sequence-number wrap affect continuity; the offset does
    /// not guarantee that the restored stream advances beyond every message subscribers accepted.
    /// The default
    /// <see cref="NullPubSubWriterCheckpointStore"/> is a no-op so the non-redundant publish
    /// path is unaffected. Implementations are a provider extension point and must be injectable.
    /// </remarks>
    public interface IPubSubWriterCheckpointStore
    {
        /// <summary>
        /// Reads the last checkpointed SequenceNumber for a writer, or
        /// <see langword="null"/> when none has been recorded.
        /// </summary>
        /// <param name="writerGroupComponentId">Deterministic writer-group component id.</param>
        /// <param name="dataSetWriterId">DataSetWriter id within the group.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<uint?> GetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records the current SequenceNumber for a writer.
        /// </summary>
        /// <param name="writerGroupComponentId">Deterministic writer-group component id.</param>
        /// <param name="dataSetWriterId">DataSetWriter id within the group.</param>
        /// <param name="sequenceNumber">The SequenceNumber last emitted by the writer.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            uint sequenceNumber,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Records the current SequenceNumber for a writer using a lease fencing token to reject
        /// stale active writers when the caller has already resolved ownership.
        /// </summary>
        /// <param name="writerGroupComponentId">Deterministic writer-group component id.</param>
        /// <param name="dataSetWriterId">DataSetWriter id within the group.</param>
        /// <param name="sequenceNumber">The SequenceNumber last emitted by the writer.</param>
        /// <param name="fencingToken">Current lease fencing token.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SetSequenceNumberAsync(
            string writerGroupComponentId,
            ushort dataSetWriterId,
            uint sequenceNumber,
            long fencingToken,
            CancellationToken cancellationToken = default);
    }
}
