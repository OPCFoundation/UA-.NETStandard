/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// The kind of node-state change reported by an
    /// <see cref="INodeStateStore"/> change-feed.
    /// </summary>
    public enum NodeStateChangeKind
    {
        /// <summary>
        /// A node was created or its topology/attributes were updated.
        /// </summary>
        Upsert,

        /// <summary>
        /// A node was removed.
        /// </summary>
        Delete,

        /// <summary>
        /// A variable value (Value/StatusCode/Timestamp) changed.
        /// </summary>
        Value
    }

    /// <summary>
    /// A single change observed on an <see cref="INodeStateStore"/>
    /// change-feed. Topology changes (<see cref="NodeStateChangeKind.Upsert"/>
    /// / <see cref="NodeStateChangeKind.Delete"/>) are applied live to every
    /// replica; <see cref="NodeStateChangeKind.Value"/> changes carry the
    /// new <see cref="Value"/>.
    /// </summary>
    public sealed record NodeStateChange
    {
        /// <summary>
        /// The kind of change.
        /// </summary>
        public NodeStateChangeKind Kind { get; init; }

        /// <summary>
        /// The affected node identifier.
        /// </summary>
        public NodeId NodeId { get; init; } = NodeId.Null;

        /// <summary>
        /// The serialized node for <see cref="NodeStateChangeKind.Upsert"/>
        /// changes; otherwise <c>null</c>.
        /// </summary>
        public IStoredNode? Node { get; init; }

        /// <summary>
        /// The new value for <see cref="NodeStateChangeKind.Value"/> changes;
        /// otherwise a null <see cref="DataValue"/>
        /// (<see cref="DataValue.IsNull"/>).
        /// </summary>
        public DataValue Value { get; init; } = DataValue.Null;
    }
}
