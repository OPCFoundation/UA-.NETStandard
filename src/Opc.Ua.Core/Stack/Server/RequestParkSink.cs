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
        /// Creates a sink that only signals.
        /// </summary>
        public RequestParkSink()
        {
        }

        /// <summary>
        /// Creates a sink for a decoded request. When the request is a
        /// <see cref="PublishRequest"/>, parking also drops the parts of it that its
        /// handler has consumed by then (see <see cref="ReleasesPayload"/>).
        /// </summary>
        internal RequestParkSink(IServiceRequest request)
        {
            m_publish = request as PublishRequest;
        }

        /// <summary>
        /// A task that completes the first time the request parks.
        /// </summary>
        public Task ParkedTask => m_tcs.Task;

        /// <summary>
        /// Whether parking drops the decoded payload of the request, so that a parked
        /// request retains its request header but neither the
        /// <see cref="RequestHeader.AdditionalHeader"/> nor the acknowledgements.
        /// </summary>
        /// <remarks>
        /// A held Publish waits up to a keep-alive period, or with no timeout hint
        /// until the next notification. Decoded, its additional header can be many times
        /// its wire size (arrays of variants), and nothing reads it or the acknowledgements
        /// after the handler parks: the acknowledgements are applied and the header was
        /// validated before. Without the payload a parked Publish does not need the
        /// decoded-request cost it was admitted with, only one for what it still retains
        /// (<see cref="EstimateRetainedBytes"/>).
        /// </remarks>
        internal bool ReleasesPayload => m_publish != null;

        /// <summary>
        /// Estimates the bytes a Publish still retains once it parked, until it completes:
        /// a fixed share for the header and the handler state, the result (and, when
        /// diagnostics are requested, the diagnostic information) of each acknowledgement,
        /// and the audit entry id. Zero when the request does not drop its payload.
        /// </summary>
        /// <remarks>
        /// Called at admission, before the request runs, so the estimate is fixed for the
        /// whole life of the request.
        /// </remarks>
        internal long EstimateRetainedBytes()
        {
            PublishRequest? publish = m_publish;
            if (publish == null)
            {
                return 0;
            }
            RequestHeader? header = publish.RequestHeader;
            bool diagnostics = header != null && (header.ReturnDiagnostics & kOperationDiagnostics) != 0;
            long retained = kRetainedBaseBytes +
                ((long)publish.SubscriptionAcknowledgements.Count *
                    (diagnostics ? kRetainedBytesPerDiagnosedResult : kRetainedBytesPerResult));
            if (header?.AuditEntryId != null)
            {
                retained += 2L * header.AuditEntryId.Length;
            }
            return retained;
        }

        /// <inheritdoc/>
        public void NotifyParked()
        {
            PublishRequest? publish = m_publish;
            if (publish != null)
            {
                // runs on the handler, at its waiting point, before the worker sees the park
                publish.SubscriptionAcknowledgements = default;
                if (publish.RequestHeader != null)
                {
                    publish.RequestHeader.AdditionalHeader = ExtensionObject.Null;
                }
            }
            m_tcs.TrySetResult(true);
        }

        /// <summary>
        /// The request header, response header, contexts and handler state of a parked Publish.
        /// </summary>
        private const long kRetainedBaseBytes = 2048;

        /// <summary>
        /// The status code of an acknowledgement in the result array.
        /// </summary>
        private const long kRetainedBytesPerResult = 8;

        /// <summary>
        /// The status code and a diagnostic information object of an acknowledgement.
        /// </summary>
        private const long kRetainedBytesPerDiagnosedResult = 256;

        /// <summary>
        /// The operation-level bits of <see cref="RequestHeader.ReturnDiagnostics"/>.
        /// </summary>
        private const uint kOperationDiagnostics = (uint)DiagnosticsMasks.OperationAll;

        private readonly TaskCompletionSource<bool> m_tcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly PublishRequest? m_publish;

        /// <summary>
        /// The bytes the request still retains once it parked without its payload, or 0 when
        /// it keeps its full decoded-request cost. Set at admission, before the request is
        /// queued, and not changed afterwards.
        /// </summary>
        internal long RetainedCost;

        /// <summary>
        /// The decoded-request charge for <see cref="RetainedCost"/>, held by the queue entry
        /// of the request instead of its full cost once it parked.
        /// </summary>
        internal FairRequestQueue.StageHold RetainedCostHold;
    }
}
