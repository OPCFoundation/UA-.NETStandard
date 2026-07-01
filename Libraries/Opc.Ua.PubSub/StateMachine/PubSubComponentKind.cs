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

namespace Opc.Ua.PubSub.StateMachine
{
    /// <summary>
    /// Classifies the kind of PubSub component a <see cref="PubSubStateMachine"/>
    /// tracks. Used for diagnostics labelling and to determine which counter
    /// classification a transition increments.
    /// </summary>
    /// <remarks>
    /// Implements <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.10.1">
    /// Part 14 §9.1.10.1 PubSubStatusType</see> — every Object that owns a
    /// <c>PubSubStatusType</c> in the address space (PublishSubscribe,
    /// PubSubConnection, PubSubGroup, DataSetWriter, DataSetReader) has a
    /// matching value here.
    /// </remarks>
    public enum PubSubComponentKind
    {
        /// <summary>
        /// The root <c>PublishSubscribe</c> object that owns all connections.
        /// </summary>
        Application,

        /// <summary>
        /// A single <c>PubSubConnection</c> binding a publisher and/or subscriber
        /// to a transport profile and address.
        /// </summary>
        Connection,

        /// <summary>
        /// A <c>WriterGroup</c> grouping <see cref="DataSetWriter"/>s under one
        /// publishing schedule and message mapping.
        /// </summary>
        WriterGroup,

        /// <summary>
        /// A <c>DataSetWriter</c> emitting DataSetMessages for one PublishedDataSet.
        /// </summary>
        DataSetWriter,

        /// <summary>
        /// A <c>ReaderGroup</c> grouping <see cref="DataSetReader"/>s under one
        /// transport subscription and message mapping.
        /// </summary>
        ReaderGroup,

        /// <summary>
        /// A <c>DataSetReader</c> filtering and decoding inbound DataSetMessages.
        /// </summary>
        DataSetReader
    }
}
