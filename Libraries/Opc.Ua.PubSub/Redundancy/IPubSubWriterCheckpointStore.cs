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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Shares the live per-DataSetWriter message SequenceNumber across a redundant PubSub
    /// set so a promoted Hot standby continues the sequence without a reset, per OPC UA
    /// Part 14 §9.1.6 (Hot redundancy) and §7.2.5.4.1 (SequenceNumber handling).
    /// </summary>
    /// <remarks>
    /// The active publisher periodically checkpoints its writers' SequenceNumbers. On
    /// take-over a standby seeds each writer from the last checkpoint plus a safety margin so
    /// the emitted numbers are strictly increasing — a forward gap (which subscribers tolerate)
    /// rather than a reset (which forces subscribers to reset de-duplication). The default
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
