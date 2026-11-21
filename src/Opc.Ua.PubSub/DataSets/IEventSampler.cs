// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Provider that turns one or more pending OPC UA events into a
    /// projection of <see cref="Variant"/> rows — one row per event,
    /// columns aligned with
    /// <see cref="PublishedEventsDataType.SelectedFields"/>.
    /// </summary>
    /// <remarks>
    /// Implements the publisher-side event acquisition contract
    /// implied by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.4">
    /// Part 14 §6.2.4 PublishedEvents</see> and the
    /// <see cref="ContentFilter"/> / <see cref="SimpleAttributeOperand"/>
    /// model from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-4/v1.05.06/7.7">
    /// Part 4 §7.7 ContentFilter</see>.
    /// </remarks>
    public interface IEventSampler
    {
        /// <summary>
        /// Configured event-source name (matches
        /// <see cref="PublishedDataSetDataType.Name"/>).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Samples zero or more events that have fired since the last
        /// call and projects them across the supplied
        /// <see cref="SimpleAttributeOperand"/>s. The supplied filter
        /// (if non-null) is applied as a where-clause and must yield
        /// <see langword="true"/> for the event to be returned.
        /// </summary>
        /// <param name="selectedFields">Field projection.</param>
        /// <param name="filter">Optional where-clause.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>One row of <see cref="Variant"/>s per emitted event;
        /// empty when no event matched.</returns>
        ValueTask<IReadOnlyList<IReadOnlyList<Variant>>> SampleEventsAsync(
            ArrayOf<SimpleAttributeOperand> selectedFields,
            ContentFilter? filter,
            CancellationToken cancellationToken = default);
    }
}
