/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// RFC 9147 §5.8.1 exponential retransmission timeout calculator.
    /// </summary>
    internal sealed class DtlsRetransmissionTimer
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsRetransmissionTimer"/> with the initial and
        /// maximum retransmission timeouts.
        /// </summary>
        public DtlsRetransmissionTimer(TimeSpan initialTimeout, TimeSpan maximumTimeout)
        {
            if (initialTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(initialTimeout));
            }

            if (maximumTimeout < initialTimeout)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumTimeout));
            }

            InitialTimeout = initialTimeout;
            MaximumTimeout = maximumTimeout;
            CurrentTimeout = initialTimeout;
        }

        /// <summary>
        /// Initial retransmission timeout applied after a reset.
        /// </summary>
        public TimeSpan InitialTimeout { get; }

        /// <summary>
        /// Upper bound the timeout is clamped to during exponential backoff.
        /// </summary>
        public TimeSpan MaximumTimeout { get; }

        /// <summary>
        /// Timeout that will be used for the next flight retransmission.
        /// </summary>
        public TimeSpan CurrentTimeout { get; private set; }

        /// <summary>
        /// Returns the current timeout and doubles it for the next retransmission, clamped
        /// to <see cref="MaximumTimeout"/>.
        /// </summary>
        public TimeSpan NextTimeout()
        {
            TimeSpan current = CurrentTimeout;
            long doubledTicks = current.Ticks > long.MaxValue / 2 ? long.MaxValue : current.Ticks * 2;
            CurrentTimeout = TimeSpan.FromTicks(Math.Min(doubledTicks, MaximumTimeout.Ticks));
            return current;
        }

        /// <summary>
        /// Resets the timeout back to <see cref="InitialTimeout"/>.
        /// </summary>
        public void Reset()
        {
            CurrentTimeout = InitialTimeout;
        }
    }
}
