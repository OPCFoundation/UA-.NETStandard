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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Why a frame was rejected. The dividing line drawn by
    /// Part 6 errata 5.12 is whether the sender's framing can still be
    /// trusted: a bad ChannelId is a bug in one stream, a bad header
    /// means the byte stream is no longer being parsed the way the sender
    /// wrote it and no further frame on it can be believed.
    /// </summary>
    public enum DataChannelFrameError
    {
        /// <summary>
        /// The frame is well formed.
        /// </summary>
        None = 0,

        /// <summary>
        /// The body is shorter than the twelve byte stream header.
        /// </summary>
        MalformedHeader,

        /// <summary>
        /// The two byte padding field of the stream header, held back for
        /// a future revision, is not zero.
        /// </summary>
        NonZeroHeaderPadding,

        /// <summary>
        /// One of the flag bits held back for a future revision is set.
        /// </summary>
        UnknownFlagBits,

        /// <summary>
        /// The FrameSequenceNumber is zero, which the value space
        /// excludes.
        /// </summary>
        ZeroFrameSequenceNumber,

        /// <summary>
        /// The frame is too short to hold the header its own FrameType
        /// and flags imply.
        /// </summary>
        FrameTooShort,

        /// <summary>
        /// The frame is larger than the negotiated buffer allows.
        /// </summary>
        FrameTooLarge,

        /// <summary>
        /// The RequestId of the enclosing chunk is not zero.
        /// </summary>
        NonZeroRequestId,

        /// <summary>
        /// The IsFinal byte of the enclosing chunk is not 'F'. Accepting
        /// 'A' would let the abort parser read a 32 bit string length out
        /// of attacker controlled stream header bytes.
        /// </summary>
        InvalidIsFinal,

        /// <summary>
        /// A frame type other than CREDIT, PING or PONG, or any payload,
        /// arrived on the connection control channel.
        /// </summary>
        InvalidControlChannelFrame,

        /// <summary>
        /// The FrameType is one of the values 7 to 255 held back for a
        /// future revision.
        /// </summary>
        UnknownFrameType,

        /// <summary>
        /// Payload bytes follow a frame type that carries none.
        /// </summary>
        PayloadOnNonDataFrame
    }

    /// <summary>
    /// Classifies a <see cref="DataChannelFrameError"/> into the two
    /// responses Part 6 errata 5.12 defines.
    /// </summary>
    public static class DataChannelFrameErrorExtensions
    {
        /// <summary>
        /// True when the fault means the byte stream can no longer be
        /// parsed the way the sender wrote it, so the SecureChannel is
        /// closed under OPC 10000-6 6.7.7 rather than the channel reset.
        /// </summary>
        /// <param name="error">The decode error.</param>
        public static bool IsFatal(this DataChannelFrameError error)
        {
            switch (error)
            {
                case DataChannelFrameError.MalformedHeader:
                case DataChannelFrameError.NonZeroHeaderPadding:
                case DataChannelFrameError.UnknownFlagBits:
                case DataChannelFrameError.ZeroFrameSequenceNumber:
                case DataChannelFrameError.FrameTooShort:
                case DataChannelFrameError.FrameTooLarge:
                case DataChannelFrameError.NonZeroRequestId:
                case DataChannelFrameError.InvalidIsFinal:
                case DataChannelFrameError.InvalidControlChannelFrame:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The StatusCode a channel local fault is reset with. Fatal
        /// faults close the SecureChannel and return
        /// <see cref="StatusCodes.BadTcpMessageTypeInvalid"/>.
        /// </summary>
        /// <param name="error">The decode error.</param>
        public static StatusCode ToStatusCode(this DataChannelFrameError error)
        {
            switch (error)
            {
                case DataChannelFrameError.None:
                    return StatusCodes.Good;
                case DataChannelFrameError.UnknownFrameType:
                    return StatusCodes.BadDataChannelFrameTypeUnsupported;
                case DataChannelFrameError.PayloadOnNonDataFrame:
                    return StatusCodes.BadDataChannelFrameInvalid;
                default:
                    return StatusCodes.BadTcpMessageTypeInvalid;
            }
        }
    }
}
