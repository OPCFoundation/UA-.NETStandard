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
