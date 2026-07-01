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

using System;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Groups
{
    /// <summary>
    /// Runtime view of one <see cref="DataSetReaderDataType"/>: the
    /// writer the reader binds to, the sink it writes decoded
    /// values into, and the receive-timeout governing the state
    /// machine.
    /// </summary>
    /// <remarks>
    /// Implements the DataSetReader contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9">
    /// Part 14 §6.2.9 DataSetReader</see>.
    /// </remarks>
    public interface IDataSetReader
    {
        /// <summary>
        /// DataSetWriterId — the publisher writer this reader matches
        /// (the reader does NOT have its own writer id).
        /// </summary>
        ushort DataSetWriterId { get; }

        /// <summary>
        /// Reader name (matches
        /// <see cref="DataSetReaderDataType.Name"/>).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Sink that consumes decoded DataSet fields.
        /// </summary>
        ISubscribedDataSetSink Sink { get; }

        /// <summary>
        /// Receive timeout — if no DataSetMessage arrives within
        /// this interval the reader transitions to <c>Error</c> and
        /// emits the <c>MessageReceiveTimeouts</c> diagnostic.
        /// </summary>
        TimeSpan MessageReceiveTimeout { get; }

        /// <summary>
        /// Original configuration record this runtime view was
        /// instantiated from.
        /// </summary>
        DataSetReaderDataType Configuration { get; }

        /// <summary>
        /// State machine participating in the ReaderGroup cascade.
        /// </summary>
        PubSubStateMachine State { get; }
    }
}
