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
using System.Diagnostics;

namespace Opc.Ua
{
    /// <summary>
    /// The request queue's view of a request's time budget and of the retry-after hint it
    /// sends when it rejects a request because it is full.
    /// </summary>
    /// <remarks>
    /// Runs once or twice for every request: integer <see cref="Stopwatch"/> ticks only, and
    /// nothing is allocated unless a request is rejected.
    /// </remarks>
    internal static class RequestQueueBudget
    {
        /// <summary>
        /// The shortest retry-after hint sent with a queue-full rejection.
        /// </summary>
        internal static readonly TimeSpan MinQueueFullRetryAfter = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// The longest retry-after hint sent with a queue-full rejection.
        /// </summary>
        internal static readonly TimeSpan MaxQueueFullRetryAfter = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// Gets the request's <see cref="RequestHeader.TimeoutHint"/> in milliseconds and its
        /// receipt timestamp when the queue counts its waiting time against the hint.
        /// </summary>
        /// <returns>
        /// <c>false</c> when the request has no queue budget: no positive hint, a request that
        /// does not report its receipt, or a parkable request (a held Publish), whose hint the
        /// subscription layer applies from when it is handled.
        /// </returns>
        private static bool TryGetBudget(
            IEndpointIncomingRequest request,
            out int timeoutHint,
            out long receivedTimestamp)
        {
            timeoutHint = 0;
            receivedTimestamp = 0;
            if (request is not IQueueAwareIncomingRequest queueAware)
            {
                return false;
            }
            // as before, a hint above Int32.MaxValue ms means no timeout
            timeoutHint = (int)(request.Request?.RequestHeader?.TimeoutHint ?? 0);
            if (timeoutHint <= 0 || request is IParkableIncomingRequest { ParkSink: not null })
            {
                return false;
            }
            receivedTimestamp = queueAware.ReceivedTimestamp;
            return true;
        }

        /// <summary>
        /// Gets how much of the request's <see cref="RequestHeader.TimeoutHint"/> is left,
        /// counted from receipt so that time spent queued is included.
        /// </summary>
        /// <returns><c>false</c> when the request has no queue budget.</returns>
        public static bool TryGetRemaining(IEndpointIncomingRequest request, out TimeSpan remaining)
        {
            if (!TryGetBudget(request, out int timeoutHint, out long receivedTimestamp))
            {
                remaining = TimeSpan.Zero;
                return false;
            }
            long elapsed = Stopwatch.GetTimestamp() - receivedTimestamp;
            remaining = TimeSpan.FromMilliseconds(timeoutHint) -
                TimeSpan.FromTicks((long)(elapsed * s_ticksPerTimestamp));
            return true;
        }

        /// <summary>
        /// Whether the client's timeout hint ran out while the request was queued.
        /// </summary>
        public static bool HasExpired(IEndpointIncomingRequest request)
        {
            return TryGetBudget(request, out int timeoutHint, out long receivedTimestamp) &&
                Stopwatch.GetTimestamp() - receivedTimestamp >= timeoutHint * s_timestampsPerMillisecond;
        }

        /// <summary>
        /// Creates the fault of a queue-full rejection with a short, jittered retry-after
        /// hint, so rejected clients do not retry in lockstep.
        /// </summary>
        public static ServerBusyException CreateQueueFullException()
        {
            // the low timestamp bits are jitter enough and, unlike a shared random, take no
            // lock on the path that runs most when the server is overloaded
            int minMs = (int)MinQueueFullRetryAfter.TotalMilliseconds;
            int spreadMs = (int)MaxQueueFullRetryAfter.TotalMilliseconds - minMs + 1;
            int jitterMs = (int)((ulong)Stopwatch.GetTimestamp() % (ulong)spreadMs);
            return new ServerBusyException(s_queueFull, TimeSpan.FromMilliseconds(minMs + jitterMs));
        }

        /// <summary>
        /// The result of every queue-full rejection; immutable, so it is shared.
        /// </summary>
        private static readonly ServiceResult s_queueFull = new(
            StatusCodes.BadServerTooBusy,
            new LocalizedText("The server request queue is full."));

        /// <summary>
        /// <see cref="TimeSpan"/> ticks per <see cref="Stopwatch"/> timestamp unit.
        /// </summary>
        private static readonly double s_ticksPerTimestamp =
            (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

        /// <summary>
        /// <see cref="Stopwatch"/> timestamp units per millisecond.
        /// </summary>
        private static readonly long s_timestampsPerMillisecond = Stopwatch.Frequency / 1000;
    }
}
