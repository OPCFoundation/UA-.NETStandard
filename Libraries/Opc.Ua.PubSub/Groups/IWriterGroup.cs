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
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Runtime view of one <see cref="WriterGroupDataType"/>: the
    /// publishing cadence, the set of writers it owns, and the
    /// state machine driving the cascade to children.
    /// </summary>
    /// <remarks>
    /// Implements the WriterGroup contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.6">
    /// Part 14 §6.2.6 WriterGroup</see>.
    /// </remarks>
    public interface IWriterGroup
    {
        /// <summary>
        /// WriterGroupId — unique within the parent PubSubConnection
        /// and carried in every NetworkMessage header.
        /// </summary>
        ushort WriterGroupId { get; }

        /// <summary>
        /// Group name (matches the configured
        /// <see cref="WriterGroupDataType"/> <c>Name</c> field).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Snapshot of writers in this group.
        /// </summary>
        IReadOnlyList<IDataSetWriter> DataSetWriters { get; }

        /// <summary>
        /// Publishing schedule (period, keep-alive, offsets).
        /// </summary>
        PubSubSchedule Schedule { get; }

        /// <summary>
        /// Original configuration record this runtime view was
        /// instantiated from.
        /// </summary>
        WriterGroupDataType Configuration { get; }

        /// <summary>
        /// State machine participating in the PubSubConnection
        /// cascade.
        /// </summary>
        PubSubStateMachine State { get; }
    }
}
