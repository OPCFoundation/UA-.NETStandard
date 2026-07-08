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
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// Sends a UDP NetworkMessage once and then re-transmits it
    /// <c>MessageRepeatCount</c> additional times spaced by
    /// <c>MessageRepeatDelay</c>. UDP has no IP-layer retransmission
    /// so Part 14 §6.4.1 lets publishers replay frames to improve
    /// delivery probability on lossy networks.
    /// </summary>
    /// <remarks>
    /// Implements <c>MessageRepeatCount</c> / <c>MessageRepeatDelay</c>
    /// per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1">
    /// Part 14 §6.4.1 Datagram transport parameters</see>. Wakes from
    /// inter-repeat sleeps via <see cref="TimeProvider"/> so tests can
    /// drive the timer deterministically with
    /// <c>FakeTimeProvider</c>.
    /// </remarks>
    public sealed class UdpMessageRepeater
    {
        private readonly int m_count;
        private readonly TimeSpan m_delay;
        private readonly TimeProvider m_timeProvider;

        /// <summary>
        /// Initializes a new <see cref="UdpMessageRepeater"/>.
        /// </summary>
        /// <param name="count">
        /// Number of re-transmissions to perform after the initial
        /// send (the total send count is <c>count + 1</c>). Negative
        /// values are coerced to <c>0</c>.
        /// </param>
        /// <param name="delay">
        /// Delay between successive sends. Negative spans are
        /// coerced to <see cref="TimeSpan.Zero"/>.
        /// </param>
        /// <param name="timeProvider">
        /// Clock used to schedule the inter-repeat delays. Must not
        /// be <see langword="null"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="timeProvider"/> is <see langword="null"/>.
        /// </exception>
        public UdpMessageRepeater(int count, TimeSpan delay, TimeProvider timeProvider)
        {
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            m_count = count > 0 ? count : 0;
            m_delay = delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            m_timeProvider = timeProvider;
        }

        /// <summary>
        /// Number of re-transmissions performed after the initial
        /// send.
        /// </summary>
        public int RepeatCount => m_count;

        /// <summary>
        /// Delay between successive sends.
        /// </summary>
        public TimeSpan RepeatDelay => m_delay;

        /// <summary>
        /// Invokes <paramref name="sendOnce"/> once and then
        /// <see cref="RepeatCount"/> additional times with
        /// <see cref="RepeatDelay"/> spacing. Propagates cancellation
        /// at any point. Subsequent sends are suppressed if
        /// <paramref name="sendOnce"/> throws on the first attempt.
        /// </summary>
        /// <param name="sendOnce">
        /// Single-send delegate; invoked with the supplied
        /// <paramref name="cancellationToken"/>.
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask SendWithRepeatsAsync(
            Func<CancellationToken, ValueTask> sendOnce,
            CancellationToken cancellationToken = default)
        {
            if (sendOnce is null)
            {
                throw new ArgumentNullException(nameof(sendOnce));
            }
            cancellationToken.ThrowIfCancellationRequested();
            await sendOnce(cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < m_count; i++)
            {
                if (m_delay > TimeSpan.Zero)
                {
                    await m_timeProvider.Delay(m_delay, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                await sendOnce(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
