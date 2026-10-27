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

namespace Opc.Ua
{
    /// <summary>
    /// The request queue's view of a request's time budget and of the retry-after hint it
    /// sends when it rejects a request because it is full.
    /// </summary>
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
        /// Gets how much of the request's <see cref="RequestHeader.TimeoutHint"/> is left,
        /// counted from receipt so that time spent queued is included.
        /// </summary>
        /// <returns>
        /// <c>false</c> when the request has no queue budget: no positive hint, a request that
        /// does not report its receipt, or a parkable request (a held Publish), whose hint the
        /// subscription layer applies from when it is handled.
        /// </returns>
        public static bool TryGetRemaining(IEndpointIncomingRequest request, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            if (request is not IQueueAwareIncomingRequest queueAware ||
                request is IParkableIncomingRequest { ParkSink: not null })
            {
                return false;
            }
            // as before, a hint above Int32.MaxValue ms means no timeout
            int timeoutHint = (int)(request.Request?.RequestHeader?.TimeoutHint ?? 0);
            if (timeoutHint <= 0)
            {
                return false;
            }
            remaining = TimeSpan.FromMilliseconds(timeoutHint) -
                TimeProvider.System.GetElapsedTime(queueAware.ReceivedTimestamp);
            return true;
        }

        /// <summary>
        /// Whether the client's timeout hint ran out while the request was queued.
        /// </summary>
        public static bool HasExpired(IEndpointIncomingRequest request)
        {
            return TryGetRemaining(request, out TimeSpan remaining) && remaining <= TimeSpan.Zero;
        }

        /// <summary>
        /// Creates the fault of a queue-full rejection with a short, jittered retry-after
        /// hint, so rejected clients do not retry in lockstep.
        /// </summary>
        public static ServerBusyException CreateQueueFullException()
        {
            var retryAfter = TimeSpan.FromMilliseconds(UnsecureRandom.Shared.Next(
                (int)MinQueueFullRetryAfter.TotalMilliseconds,
                (int)MaxQueueFullRetryAfter.TotalMilliseconds + 1));
            return new ServerBusyException(
                new ServiceResult(
                    StatusCodes.BadServerTooBusy,
                    new LocalizedText("The server request queue is full.")),
                retryAfter);
        }
    }
}
