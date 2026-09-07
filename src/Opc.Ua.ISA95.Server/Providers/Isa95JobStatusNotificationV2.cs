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

using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// An immutable Job Control V2 status notification describing a single
    /// committed job order state change. Exactly one notification is published
    /// per committed state change to every active subscriber, which makes it
    /// suitable for projection to server events.
    /// </summary>
    public sealed record Isa95JobStatusNotificationV2
    {
        /// <summary>
        /// The identifier of the job order whose state changed.
        /// </summary>
        public required string JobOrderId { get; init; }

        /// <summary>
        /// The job order after the committed change.
        /// </summary>
        public required V2.ISA95JobOrderDataType JobOrder { get; init; }

        /// <summary>
        /// The latest job response, or an empty response for the order.
        /// </summary>
        public required V2.ISA95JobResponseDataType JobResponse { get; init; }

        /// <summary>
        /// The latest audit-relevant localized comment retained with the job order,
        /// carrying the OPC-10031-4 V2 method <c>Comment</c> argument of the most
        /// recent operation that supplied one. Empty when none was provided.
        /// </summary>
        public required ArrayOf<LocalizedText> Comment { get; init; }

        /// <summary>
        /// The top-level state machine state number after the change.
        /// </summary>
        public required uint StateNumber { get; init; }

        /// <summary>
        /// The top-level state machine state text after the change.
        /// </summary>
        public required LocalizedText StateText { get; init; }

        /// <summary>
        /// The full state path after the change, ordered from the top-level
        /// state to the most specific sub-state.
        /// </summary>
        public required ArrayOf<V2.ISA95StateDataType> State { get; init; }

        /// <summary>
        /// The time at which the change was committed, as reported by the
        /// injected <see cref="System.TimeProvider"/>.
        /// </summary>
        public required DateTimeUtc Timestamp { get; init; }

        /// <summary>
        /// A monotonically increasing sequence number assigned to the change.
        /// </summary>
        public required ulong SequenceNumber { get; init; }
    }
}
