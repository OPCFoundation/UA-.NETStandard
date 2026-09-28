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

using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// An incoming request that can notify the request-processing worker when it
    /// parks (suspends waiting for an out-of-band completion). Custom incoming-request
    /// implementations can opt in using the same sink as the stack's endpoint wrapper.
    /// Nonparticipants retain the worker until completion.
    /// </summary>
    /// <remarks>
    /// A participating request must expose the same sink before admission and throughout execution,
    /// and must not signal it before reaching its terminal waiting point in CallAsync.
    /// With resource isolation enabled, a non-null sink reserves parked-request capacity before queuing,
    /// even if the request ultimately completes without parking. DecoupleHeldPublishRequests disables
    /// worker decoupling for these requests too.
    /// </remarks>
    public interface IParkableIncomingRequest : IEndpointIncomingRequest
    {
        /// <summary>
        /// Gets the park sink observed by the request-processing worker, or <c>null</c>
        /// when the request cannot park (so the worker uses the legacy inline path with
        /// no additional per-request overhead).
        /// </summary>
        RequestParkSink? ParkSink { get; }
    }

    /// <summary>
    /// One-shot signal a request handler raises when it parks so the
    /// request-processing worker can be released while the parked request
    /// completes independently.
    /// </summary>
    public sealed class RequestParkSink : IRequestParkSink
    {
        /// <summary>
        /// A task that completes the first time the request parks.
        /// </summary>
        public Task ParkedTask => m_tcs.Task;

        /// <inheritdoc/>
        public void NotifyParked()
        {
            m_tcs.TrySetResult(true);
        }

        private readonly TaskCompletionSource<bool> m_tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
