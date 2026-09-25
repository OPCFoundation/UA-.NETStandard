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
using System.Threading;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Bounds the memory that the chunks of incomplete messages hold across
    /// all the secure channels that share the budget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A secure channel keeps the chunks of a message until its final chunk
    /// arrives (OPC 10000-6 §6.7.2). The MaxMessageSize and MaxChunkCount
    /// negotiated by the Hello/Acknowledge exchange bound what one channel
    /// keeps, but nothing in the protocol bounds it across channels: a peer
    /// that opens many channels and never sends the final chunk holds the per
    /// channel maximum on every one of them, until the process runs out of
    /// memory. A budget shared by all the channels of a server caps the total.
    /// </para>
    /// <para>
    /// A channel reserves the length of the buffer each intermediate chunk it
    /// keeps occupies, and releases the reservation when the message is
    /// complete, discarded, or the channel goes away. The final chunk of a
    /// message is never reserved: it is processed at once, so a message that
    /// fits in one chunk is not refused by this budget. A server channel whose chunk does
    /// not fit discards its incomplete message and is closed with
    /// <see cref="StatusCodes.BadTcpNotEnoughResources"/>, the error
    /// OPC 10000-6 §7.1.5 defines for a server that runs out of resources.
    /// </para>
    /// <para>
    /// A channel on which no session has been activated may only fill the
    /// budget up to <see cref="MaxBytesWithoutSession"/>, so a peer that never
    /// activates a session cannot take the memory the sessions of the server
    /// need.
    /// </para>
    /// </remarks>
    public sealed class ChunkReassemblyBudget
    {
        /// <summary>
        /// Creates a budget which channels without an activated session may
        /// fill up to half of its total capacity.
        /// </summary>
        /// <remarks>
        /// This is a global occupancy threshold, not a reserved partition.
        /// Activated-session traffic may consume that portion and prevent new
        /// sessionless multi-chunk messages until capacity is released.
        /// Single-chunk messages are not charged to this budget.
        /// </remarks>
        /// <param name="maxBytes">
        /// The number of bytes the chunks of incomplete messages may hold in
        /// total.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxBytes"/> is not positive.
        /// </exception>
        public ChunkReassemblyBudget(long maxBytes)
            : this(maxBytes, maxBytes / 2)
        {
        }

        /// <summary>
        /// Creates a budget with an explicit share for channels without an
        /// activated session.
        /// </summary>
        /// <param name="maxBytes">
        /// The number of bytes the chunks of incomplete messages may hold in
        /// total.
        /// </param>
        /// <param name="maxBytesWithoutSession">
        /// The level up to which channels without an activated session may
        /// fill the budget. Zero keeps them from assembling any message that
        /// does not fit in one chunk; <paramref name="maxBytes"/> gives them
        /// the whole budget.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxBytes"/> is not positive, or
        /// <paramref name="maxBytesWithoutSession"/> is negative or larger than
        /// <paramref name="maxBytes"/>.
        /// </exception>
        public ChunkReassemblyBudget(long maxBytes, long maxBytesWithoutSession)
        {
            if (maxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxBytes),
                    maxBytes,
                    "The chunk reassembly budget must be positive.");
            }

            if (maxBytesWithoutSession < 0 || maxBytesWithoutSession > maxBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxBytesWithoutSession),
                    maxBytesWithoutSession,
                    "The share of channels without a session must be between zero and the budget.");
            }

            MaxBytes = maxBytes;
            MaxBytesWithoutSession = maxBytesWithoutSession;
        }

        /// <summary>
        /// The number of bytes the chunks of incomplete messages may hold in
        /// total.
        /// </summary>
        public long MaxBytes { get; }

        /// <summary>
        /// The level up to which channels without an activated session may
        /// fill the budget. The rest is left to the channels of sessions.
        /// </summary>
        public long MaxBytesWithoutSession { get; }

        /// <summary>
        /// The number of bytes currently reserved.
        /// </summary>
        public long ReservedBytes => Interlocked.Read(ref m_reservedBytes);

        /// <summary>
        /// Creates a reassembly budget from the endpoint's maximum message size.
        /// </summary>
        /// <param name="configuration">
        /// The endpoint configuration, or null to use the default transport message size.
        /// </param>
        /// <returns>
        /// A new budget with half of its capacity available to channels without an activated session.
        /// </returns>
        public static ChunkReassemblyBudget CreateDefault(EndpointConfiguration? configuration)
        {
            int maxMessageSize = configuration?.MaxMessageSize ?? TcpMessageLimits.DefaultMaxMessageSize;
            return new ChunkReassemblyBudget(GetDefaultMaxBytes(maxMessageSize));
        }

        /// <summary>
        /// Returns the budget a listener uses when none is configured.
        /// </summary>
        /// <param name="maxMessageSize">
        /// The maximum message size of the listener's channels, or zero or less
        /// when the message size is not limited.
        /// </param>
        /// <returns>
        /// Room for sixteen messages of the maximum size, clamped between
        /// <see cref="MinDefaultMaxBytes"/> and <see cref="MaxDefaultMaxBytes"/>,
        /// then increased if necessary to leave room for four, so a
        /// channel without a session can still assemble one in buffers up to
        /// twice the size of its chunks. A configuration that does not limit
        /// the message size gets <see cref="MaxDefaultMaxBytes"/>.
        /// </returns>
        public static long GetDefaultMaxBytes(int maxMessageSize)
        {
            if (maxMessageSize <= 0)
            {
                return MaxDefaultMaxBytes;
            }

            long bytes = Math.Min(
                Math.Max(16L * maxMessageSize, MinDefaultMaxBytes),
                MaxDefaultMaxBytes);
            return Math.Max(bytes, 4L * maxMessageSize);
        }

        /// <summary>
        /// Reserves room for a chunk that has to be kept until the rest of its
        /// message arrives.
        /// </summary>
        /// <param name="byteCount">The number of bytes to reserve.</param>
        /// <param name="hasSession">
        /// Whether the chunk arrived on a channel on which a session has been
        /// activated. A channel without one may only fill the budget up to
        /// <see cref="MaxBytesWithoutSession"/>.
        /// </param>
        /// <returns>
        /// <c>true</c> if the bytes were reserved; <c>false</c> if they do not
        /// fit, in which case nothing is reserved.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="byteCount"/> is negative.
        /// </exception>
        public bool TryReserve(long byteCount, bool hasSession)
        {
            if (byteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }

            long limit = hasSession ? MaxBytes : MaxBytesWithoutSession;
            long reserved = Interlocked.Read(ref m_reservedBytes);
            while (true)
            {
                if (byteCount > limit - reserved)
                {
                    return false;
                }

                long observed = Interlocked.CompareExchange(
                    ref m_reservedBytes,
                    reserved + byteCount,
                    reserved);
                if (observed == reserved)
                {
                    return true;
                }

                reserved = observed;
            }
        }

        /// <summary>
        /// Releases bytes an earlier <see cref="TryReserve(long, bool)"/>
        /// reserved.
        /// </summary>
        /// <param name="byteCount">The number of bytes to release.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="byteCount"/> is negative.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// More bytes are released than are reserved; nothing is released.
        /// </exception>
        public void Release(long byteCount)
        {
            if (byteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }

            long reserved = Interlocked.Read(ref m_reservedBytes);
            while (true)
            {
                if (byteCount > reserved)
                {
                    throw new InvalidOperationException(
                        "More bytes are released than the chunk reassembly budget has reserved.");
                }

                long observed = Interlocked.CompareExchange(
                    ref m_reservedBytes,
                    reserved - byteCount,
                    reserved);
                if (observed == reserved)
                {
                    return;
                }

                reserved = observed;
            }
        }

        /// <summary>
        /// The smallest default reassembly budget: 64 MiB.
        /// </summary>
        public const long MinDefaultMaxBytes = 64L * 1024 * 1024;

        /// <summary>
        /// The default sizing ceiling before allowing for four maximum-sized messages: 1 GiB.
        /// Also used when the configured message size is unlimited.
        /// </summary>
        public const long MaxDefaultMaxBytes = 1024L * 1024 * 1024;

        private long m_reservedBytes;
    }
}
