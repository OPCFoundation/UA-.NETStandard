// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Provider abstraction invoked by
    /// <see cref="TargetVariablesSink"/> to write a decoded
    /// <see cref="DataValue"/> to a target node attribute. The
    /// concrete implementation typically calls the host server's
    /// Write service or directly updates the application's node
    /// state cache. The provider model keeps the sink decoupled
    /// from the underlying server stack so it can be unit tested
    /// and reused on both client- and server-hosted subscribers.
    /// </summary>
    /// <remarks>
    /// Backs the TargetVariables variant of SubscribedDataSet
    /// described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.10">
    /// Part 14 §6.2.10 SubscribedDataSet</see>.
    /// </remarks>
    public interface ITargetVariableWriter
    {
        /// <summary>
        /// Writes <paramref name="value"/> to the attribute
        /// <paramref name="attributeId"/> of node
        /// <paramref name="nodeId"/>. When
        /// <paramref name="writeIndexRange"/> is non-empty the write
        /// must target the indicated index range only (parsed via
        /// <see cref="NumericRange.Parse(string)"/>).
        /// </summary>
        /// <param name="nodeId">Target node identifier.</param>
        /// <param name="attributeId">Target attribute id (typically
        /// <see cref="Attributes.Value"/>).</param>
        /// <param name="writeIndexRange">Optional index range string
        /// to restrict the write to a slice of the target value.
        /// Pass <see langword="null"/> or empty for a full
        /// write.</param>
        /// <param name="value">Value to apply.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The status of the write operation.</returns>
        ValueTask<StatusCode> WriteAsync(
            NodeId nodeId,
            uint attributeId,
            string? writeIndexRange,
            DataValue value,
            CancellationToken cancellationToken = default);
    }
}
