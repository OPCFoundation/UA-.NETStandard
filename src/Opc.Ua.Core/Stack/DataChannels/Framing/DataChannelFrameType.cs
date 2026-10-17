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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The frame types a data channel frame may carry
    /// (Part 6 errata 5.3). Values 7 to 255 are reserved and a receiver
    /// rejects a frame carrying one.
    /// </summary>
    public enum DataChannelFrameType : byte
    {
        /// <summary>
        /// Carries payload. The only frame type that does.
        /// </summary>
        Data = 0,

        /// <summary>
        /// Grants flow control window. Carries ChannelCredit and
        /// ConnectionCredit.
        /// </summary>
        Credit = 1,

        /// <summary>
        /// Reports a run of frames that will never arrive. Carries
        /// FirstDiscarded and LastDiscarded, both inclusive.
        /// </summary>
        Gap = 2,

        /// <summary>
        /// Aborts or summarily closes one data channel. Carries the
        /// StatusCode that determines whether the channel reaches Closed
        /// or Faulted.
        /// </summary>
        Reset = 3,

        /// <summary>
        /// Orderly half close of one direction.
        /// </summary>
        End = 4,

        /// <summary>
        /// Round trip probe and keepalive. Carries a Timestamp.
        /// </summary>
        Ping = 5,

        /// <summary>
        /// Echo of a PING, copying its Timestamp verbatim.
        /// </summary>
        Pong = 6
    }

    /// <summary>
    /// The flags a data channel frame may set (Part 6 errata 5.4).
    /// </summary>
    [Flags]
    public enum DataChannelFrameFlags : byte
    {
        /// <summary>
        /// No flag is set.
        /// </summary>
        None = 0x00,

        /// <summary>
        /// This frame begins a logical application message.
        /// </summary>
        MessageStart = 0x01,

        /// <summary>
        /// This frame ends a logical application message. A frame with
        /// both message bits set is a complete message.
        /// </summary>
        MessageEnd = 0x02,

        /// <summary>
        /// The sender may discard this frame instead of transmitting it
        /// once its deadline passes.
        /// </summary>
        Droppable = 0x04,

        /// <summary>
        /// The eight byte Deadline field follows the stream header.
        /// </summary>
        DeadlinePresent = 0x08,

        /// <summary>
        /// An application defined synchronization point, for example a
        /// video key frame.
        /// </summary>
        Marker = 0x10
    }

    /// <summary>
    /// How a frame is delimited on the wire (Part 6 errata 5.5, 7.4).
    /// </summary>
    public enum DataChannelFramingMode
    {
        /// <summary>
        /// The frame is one UASC MessageChunk with MessageType STR,
        /// carrying the symmetric security header, the sequence header
        /// and the message footer.
        /// </summary>
        Inline = 0,

        /// <summary>
        /// The frame is the message header followed directly by the
        /// stream header. QUIC's TLS 1.3 record layer already
        /// authenticates and orders every byte, so the security header,
        /// the sequence header and the footer are omitted.
        /// </summary>
        Quic = 1
    }

    /// <summary>
    /// The direction of transfer a rule applies to. Every credit window,
    /// sequence space, pause state and closing state in this
    /// specification is maintained per direction.
    /// </summary>
    public enum DataChannelTransferDirection
    {
        /// <summary>
        /// The direction this peer sends in.
        /// </summary>
        Outbound = 0,

        /// <summary>
        /// The direction this peer receives in.
        /// </summary>
        Inbound = 1
    }
}
