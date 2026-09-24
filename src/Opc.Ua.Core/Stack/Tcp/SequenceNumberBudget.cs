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

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Tracks how much of the SecureChannel SequenceNumber space remains
    /// under the current SecurityToken, and decides when to renew and
    /// when to stall (Part 6 errata 5.1.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// OPC 10000-6 6.7.2.4 requires that a SequenceNumber is never reused
    /// under one TokenId, and discharges that requirement by assuming the
    /// token lifetime is short enough relative to the chunk rate. Data
    /// channels invalidate the assumption rather than the rule: STR
    /// frames consume SequenceNumbers at the data rate of the channel
    /// rather than at Service call rate, which is three to six orders of
    /// magnitude higher. On a 10 Gbit/s link, minimum size frames exhaust
    /// the 32 bit space in roughly two and a half minutes, well inside a
    /// typical one hour token lifetime.
    /// </para>
    /// <para>
    /// Reuse under one token is not a cosmetic fault. Where a
    /// SecurityPolicy derives a per chunk initialization vector or AEAD
    /// nonce from the SequenceNumber, reuse under one key is a
    /// cryptographic failure; where it does not, replay and injection
    /// detection for that token silently degrades, because a stack that
    /// checks only "incremented by exactly one" accepts the wrap.
    /// </para>
    /// <para>
    /// Initiating renewal is not sufficient on its own, because a slow
    /// renewal can still be overtaken. <see cref="MustStall"/> is the
    /// second half of the rule: a sender stalls its data channels rather
    /// than emitting a chunk that would reuse a value.
    /// </para>
    /// </remarks>
    internal sealed class SequenceNumberBudget
    {
        /// <summary>
        /// Creates a budget.
        /// </summary>
        /// <param name="timeProvider">The clock used to estimate the
        /// emission rate.</param>
        /// <param name="capacity">The number of SequenceNumbers usable
        /// under one token. Defaults to the 32 bit space.</param>
        public SequenceNumberBudget(
            TimeProvider? timeProvider = null,
            uint capacity = uint.MaxValue)
        {
            m_timeProvider = timeProvider ?? TimeProvider.System;
            Capacity = capacity;
            m_tokenStarted = m_timeProvider.GetTimestamp();
        }

        /// <summary>
        /// The number of SequenceNumbers usable under one token.
        /// </summary>
        public uint Capacity { get; }

        /// <summary>
        /// The number of chunks emitted under the current token.
        /// </summary>
        public long Consumed => Interlocked.Read(ref m_consumed);

        /// <summary>
        /// The number of SequenceNumbers still available under the
        /// current token.
        /// </summary>
        public long Remaining
        {
            get
            {
                long remaining = Capacity - Consumed;
                return remaining > 0 ? remaining : 0;
            }
        }

        /// <summary>
        /// The observed emission rate under the current token, in chunks
        /// per second. Zero until enough time has passed to measure one.
        /// </summary>
        public double ChunksPerSecond
        {
            get
            {
                double seconds = m_timeProvider
                    .GetElapsedTime(Interlocked.Read(ref m_tokenStarted)).TotalSeconds;

                return seconds > 0.001 ? Consumed / seconds : 0;
            }
        }

        /// <summary>
        /// True once the space remaining has fallen below the renewal
        /// threshold, which is the lesser of 2^30 values and the number
        /// of chunks the sender expects to emit in the next 60 seconds.
        /// </summary>
        /// <remarks>
        /// Taking the lesser of the two is what makes the rule work at
        /// both ends of the rate range: a slow channel renews on the
        /// fixed headroom and never renews needlessly, while a channel
        /// fast enough to burn 2^30 values inside a minute renews on its
        /// own measured rate instead.
        /// </remarks>
        public bool ShouldRenew => Remaining <= RenewalThreshold;

        /// <summary>
        /// The number of remaining values at or below which renewal is
        /// initiated.
        /// </summary>
        public long RenewalThreshold
        {
            get
            {
                double perMinute = ChunksPerSecond * SequenceNumberRenewalSeconds;

                long rateBased = perMinute >= long.MaxValue
                    ? long.MaxValue
                    : (long)perMinute;

                return Math.Min(SequenceNumberRenewalHeadroom, rateBased);
            }
        }

        /// <summary>
        /// True when no SequenceNumber remains under the current token,
        /// so the next chunk would reuse one. The caller stalls its data
        /// channels until the new token is in force rather than sending.
        /// </summary>
        public bool MustStall => Remaining == 0;

        /// <summary>
        /// Accounts one chunk emitted under the current token.
        /// </summary>
        /// <returns>False when the chunk would reuse a SequenceNumber, in
        /// which case it shall not be transmitted.</returns>
        public bool TryConsume()
        {
            long consumed = Interlocked.Increment(ref m_consumed);

            if (consumed <= Capacity)
            {
                return true;
            }

            Interlocked.Decrement(ref m_consumed);
            return false;
        }

        /// <summary>
        /// Accounts chunks emitted on another UASC send path under the
        /// current token.
        /// </summary>
        /// <param name="consumed">The observed number of symmetric chunks
        /// already emitted under the current token.</param>
        public void ObserveConsumed(long consumed)
        {
            if (consumed <= 0)
            {
                return;
            }

            while (true)
            {
                long current = Interlocked.Read(ref m_consumed);

                if (current >= consumed)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref m_consumed, consumed, current) == current)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Accounts a concrete SequenceNumber observed after a chunk was
        /// emitted under the current token.
        /// </summary>
        /// <param name="sequenceNumber">The emitted SequenceNumber.</param>
        public void ObserveSequenceNumber(uint sequenceNumber)
        {
            ObserveConsumed((long)sequenceNumber + 1);
        }

        /// <summary>
        /// Reports that a new SecurityToken is in force, which resets the
        /// budget and the rate estimate.
        /// </summary>
        public void OnTokenActivated()
        {
            Interlocked.Exchange(ref m_consumed, 0);
            Interlocked.Exchange(ref m_tokenStarted, m_timeProvider.GetTimestamp());
        }

        private readonly TimeProvider m_timeProvider;
        private long m_consumed;
        private long m_tokenStarted;

        /// <summary>
        /// The SequenceNumber headroom below which a channel initiates token
        /// renewal ahead of the normal lifetime schedule.
        /// </summary>
        private const uint SequenceNumberRenewalHeadroom = 1u << 30;

        /// <summary>
        /// The window, in seconds, of expected chunk emission that also
        /// triggers renewal when it exceeds the remaining SequenceNumber
        /// space.
        /// </summary>
        private const int SequenceNumberRenewalSeconds = 60;
    }
}
