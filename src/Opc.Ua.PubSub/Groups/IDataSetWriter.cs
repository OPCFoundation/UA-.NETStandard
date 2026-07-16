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

using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Runtime view of one <see cref="DataSetWriterDataType"/>
    /// inside a writer group: the writer's identity, the
    /// <see cref="IPublishedDataSet"/> it samples, encoding
    /// configuration, and the state machine that participates in
    /// the WriterGroup → Writer cascade.
    /// </summary>
    /// <remarks>
    /// Implements the publisher-side per-writer surface described
    /// in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.4">
    /// Part 14 §6.2.4 DataSetWriter</see>.
    /// </remarks>
    public interface IDataSetWriter
    {
        /// <summary>
        /// DataSetWriterId — unique within the parent WriterGroup
        /// and carried in every DataSetMessage header.
        /// </summary>
        ushort DataSetWriterId { get; }

        /// <summary>
        /// Writer name (matches
        /// <see cref="DataSetWriterDataType.Name"/>).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// PublishedDataSet this writer publishes.
        /// </summary>
        IPublishedDataSet PublishedDataSet { get; }

        /// <summary>
        /// Bitmask selecting which DataSetField envelope fields
        /// (Value, Status, SourceTimestamp …) are written to each
        /// DataSetMessage.
        /// </summary>
        DataSetFieldContentMask FieldContentMask { get; }

        /// <summary>
        /// Number of DeltaFrame messages emitted between successive
        /// KeyFrame messages. Zero means KeyFrame-only.
        /// </summary>
        uint KeyFrameCount { get; }

        /// <summary>
        /// Original configuration record this runtime view was
        /// instantiated from.
        /// </summary>
        DataSetWriterDataType Configuration { get; }

        /// <summary>
        /// State machine participating in the WriterGroup cascade.
        /// </summary>
        PubSubStateMachine State { get; }
    }
}
