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
    /// The credit a sender holds in one direction, counted in DATA
    /// payload bytes (Part 6 errata 5.8).
    /// </summary>
    /// <remarks>
    /// Credit is per direction. On a Bidirectional channel there are two
    /// channel windows and two connection windows, and each peer
    /// maintains the window describing what it may send. Control frames
    /// are exempt: making CREDIT itself creditable would deadlock a
    /// stalled channel permanently, and a channel that cannot be reset or
    /// probed while stalled cannot be recovered.
    /// </remarks>
    public sealed class DataChannelSendWindow
    {
        /// <summary>
        /// Creates a send window.
        /// </summary>
        /// <param name="initialCredit">The credit seeded by
        /// revisedParameters.InitialCredit. The connection window is
        /// seeded with zero instead, because the Service response is per
        /// channel and the connection window is not.</param>
        public DataChannelSendWindow(uint initialCredit = 0)
        {
            Available = initialCredit;
        }

        /// <summary>
        /// The remaining willingness of the peer to receive, in payload
        /// bytes.
        /// </summary>
        public uint Available { get; private set; }

        /// <summary>
        /// The number of times payload was ready but the window was too
        /// small to carry it. Surfaced as the CreditStalls counter.
        /// </summary>
        public uint Stalls { get; private set; }

        /// <summary>
        /// Adds a grant carried by a CREDIT frame.
        /// </summary>
        /// <param name="amount">The bytes granted.</param>
        /// <returns>False when the grant would take the window past
        /// 2^32-1, which is a protocol error the caller answers with a
        /// RESET carrying Bad_DataChannelCreditExceeded.</returns>
        public bool TryGrant(uint amount)
        {
            ulong total = (ulong)Available + amount;

            if (total > uint.MaxValue)
            {
                return false;
            }

            Available = (uint)total;
            return true;
        }

        /// <summary>
        /// Spends window on a DATA frame.
        /// </summary>
        /// <param name="payloadLength">The payload bytes the frame
        /// carries.</param>
        /// <returns>False when the window is smaller than the payload, in
        /// which case the direction is Paused and the stall is
        /// counted.</returns>
        public bool TryConsume(int payloadLength)
        {
            if (payloadLength <= 0)
            {
                return true;
            }

            if ((uint)payloadLength > Available)
            {
                Stalls++;
                return false;
            }

            Available -= (uint)payloadLength;
            return true;
        }

        /// <summary>
        /// True when the window cannot carry a frame of the given size.
        /// Entry to and exit from Paused use this same test, so a channel
        /// cannot be stalled while still reporting Open.
        /// </summary>
        /// <param name="payloadLength">The payload bytes of the frame at
        /// the head of the queue.</param>
        public bool IsBlockedBy(int payloadLength)
        {
            return payloadLength > 0 && (uint)payloadLength > Available;
        }

        /// <summary>
        /// Clears the window, used when a channel is reset.
        /// </summary>
        public void Reset()
        {
            Available = 0;
        }
    }

    /// <summary>
    /// The credit a receiver has outstanding to its peer in one
    /// direction, and the obligation to replenish it
    /// (Part 6 errata 5.8.2).
    /// </summary>
    /// <remarks>
    /// Without the replenishment obligation a receiver may legally
    /// consume its whole window and never grant another byte, leaving the
    /// channel Paused forever with neither the sender nor a certification
    /// laboratory able to call it non conforming.
    /// </remarks>
    public sealed class DataChannelReceiveCredit
    {
        /// <summary>
        /// Creates a receive credit account.
        /// </summary>
        /// <param name="initialGrant">The credit granted at open.</param>
        public DataChannelReceiveCredit(uint initialGrant = 0)
        {
            Outstanding = initialGrant;
            LastGrant = initialGrant;
        }

        /// <summary>
        /// The grant currently outstanding to the sender, in payload
        /// bytes.
        /// </summary>
        public uint Outstanding { get; private set; }

        /// <summary>
        /// The value most recently granted in one CREDIT frame. The
        /// replenishment threshold is measured against it.
        /// </summary>
        public uint LastGrant { get; private set; }

        /// <summary>
        /// Payload bytes consumed and released whose buffers have been
        /// returned but which have not yet been re granted.
        /// </summary>
        public uint Released { get; private set; }

        /// <summary>
        /// Accounts an accepted DATA frame against the outstanding grant.
        /// </summary>
        /// <param name="payloadLength">The payload bytes accepted.</param>
        /// <returns>False when the sender exceeded the window, which the
        /// caller answers with a RESET carrying
        /// Bad_DataChannelCreditExceeded.</returns>
        public bool TryAccount(int payloadLength)
        {
            if (payloadLength <= 0)
            {
                return true;
            }

            if ((uint)payloadLength > Outstanding)
            {
                return false;
            }

            Outstanding -= (uint)payloadLength;
            return true;
        }

        /// <summary>
        /// Records that the application consumed payload and the buffer
        /// holding it was released.
        /// </summary>
        /// <param name="payloadLength">The payload bytes released.</param>
        public void Release(int payloadLength)
        {
            if (payloadLength <= 0)
            {
                return;
            }

            ulong total = (ulong)Released + (uint)payloadLength;
            Released = total > uint.MaxValue ? uint.MaxValue : (uint)total;
        }

        /// <summary>
        /// Takes the replenishment a CREDIT frame should carry, if one is
        /// due.
        /// </summary>
        /// <param name="maxFrameSize">The channel's negotiated maximum
        /// frame payload, which floors the threshold so that a channel
        /// whose window has fallen below one frame is always
        /// replenished.</param>
        /// <param name="amount">The bytes to grant.</param>
        /// <returns>True when a CREDIT frame is due.</returns>
        public bool TryTakeReplenishment(uint maxFrameSize, out uint amount)
        {
            amount = 0;

            if (Released == 0)
            {
                return false;
            }

            uint threshold = LastGrant / 2;

            if (threshold < maxFrameSize)
            {
                threshold = maxFrameSize;
            }

            if (Outstanding >= threshold)
            {
                return false;
            }

            ulong total = (ulong)Outstanding + Released;

            if (total > uint.MaxValue)
            {
                amount = uint.MaxValue - Outstanding;
            }
            else
            {
                amount = Released;
            }

            if (amount == 0)
            {
                return false;
            }

            Outstanding += amount;
            Released -= amount;
            LastGrant = amount;
            return true;
        }

        /// <summary>
        /// Grants an amount unconditionally, used for the connection
        /// level bootstrap where no payload has yet been released.
        /// </summary>
        /// <param name="amount">The bytes to grant.</param>
        public void Grant(uint amount)
        {
            ulong total = (ulong)Outstanding + amount;
            Outstanding = total > uint.MaxValue ? uint.MaxValue : (uint)total;
            LastGrant = amount;
        }
    }
}
