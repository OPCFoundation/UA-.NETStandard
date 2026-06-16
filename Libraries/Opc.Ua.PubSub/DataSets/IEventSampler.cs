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
