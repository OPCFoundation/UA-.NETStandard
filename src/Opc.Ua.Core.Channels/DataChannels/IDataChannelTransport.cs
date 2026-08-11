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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The seam between the data channel engine and the transport that
    /// carries its frames.
    /// </summary>
    /// <remarks>
    /// Inline framing implements this over the UASC binary channel, where
    /// a frame is one STR MessageChunk written exactly as a MSG chunk is.
    /// The QUIC binding implements it over a per channel QUIC stream and,
    /// for the lossy modes, over datagrams.
    /// </remarks>
    public interface IDataChannelTransport
    {
        /// <summary>
        /// How frames are delimited on this transport.
        /// </summary>
        DataChannelFramingMode FramingMode { get; }

        /// <summary>
        /// The largest secured body the transport will carry, which
        /// bounds a frame's stream header, fields and payload together.
        /// </summary>
        int MaxFrameBodySize { get; }

        /// <summary>
        /// True when the transport provides its own per stream and per
        /// connection flow control, in which case CREDIT frames are
        /// neither sent nor expected and Paused follows the transport's
        /// blocking instead.
        /// </summary>
        bool HasTransportFlowControl { get; }

        /// <summary>
        /// The buffer pool payload is rented from.
        /// </summary>
        BufferManager BufferManager { get; }

        /// <summary>
        /// The clock used for deadlines, timeouts and round trip
        /// measurement.
        /// </summary>
        TimeProvider TimeProvider { get; }

        /// <summary>
        /// Writes one frame. The implementation serializes writes, so a
        /// Service chunk that becomes ready while a frame is being
        /// written is admitted immediately after it: this is how the
        /// scheduling obligation that Service traffic is never delayed by
        /// more than one maximum size frame is met.
        /// </summary>
        /// <param name="frame">The frame to write.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct);

        /// <summary>
        /// Reports a fault whose blast radius is the whole SecureChannel
        /// rather than one data channel. The implementation closes the
        /// SecureChannel under OPC 10000-6 6.7.7.
        /// </summary>
        /// <param name="error">Why the frame was rejected.</param>
        void OnProtocolFault(DataChannelFrameError error);
    }

    /// <summary>
    /// Reports data channel lifecycle transitions to the layer that turns
    /// them into OPC UA Events and audit records.
    /// </summary>
    public interface IDataChannelObserver
    {
        /// <summary>
        /// Raised for every state transition except Open to Paused and
        /// back, which is rate limited by the caller and is otherwise
        /// observed through the CreditStalls counter.
        /// </summary>
        /// <param name="channelId">The channel whose state changed.</param>
        /// <param name="state">The state entered.</param>
        /// <param name="status">The StatusCode that caused a transition
        /// into Closed or Faulted.</param>
        void OnStateChanged(uint channelId, DataChannelState state, StatusCode status);
    }
}
