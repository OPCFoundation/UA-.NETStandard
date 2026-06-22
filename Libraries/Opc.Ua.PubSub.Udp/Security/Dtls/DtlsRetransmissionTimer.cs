/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Udp.Security.Dtls
{
    /// <summary>
    /// RFC 9147 §5.8.1 exponential retransmission timeout calculator.
    /// </summary>
    internal sealed class DtlsRetransmissionTimer
    {
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

        public TimeSpan InitialTimeout { get; }

        public TimeSpan MaximumTimeout { get; }

        public TimeSpan CurrentTimeout { get; private set; }

        public TimeSpan NextTimeout()
        {
            TimeSpan current = CurrentTimeout;
            long doubledTicks = current.Ticks > long.MaxValue / 2 ? long.MaxValue : current.Ticks * 2;
            CurrentTimeout = TimeSpan.FromTicks(Math.Min(doubledTicks, MaximumTimeout.Ticks));
            return current;
        }

        public void Reset()
        {
            CurrentTimeout = InitialTimeout;
        }
    }
}
