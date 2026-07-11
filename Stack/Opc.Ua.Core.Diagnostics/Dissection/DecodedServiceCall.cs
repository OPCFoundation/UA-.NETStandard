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
using System.Collections.Generic;

namespace Opc.Ua.Pcap.Dissection
{
    /// <summary>
    /// One reassembled OPC UA service request / response observed during
    /// a capture. Populated by <c>ServiceCallReassembler</c> after offline
    /// decryption.
    /// </summary>
    public sealed class DecodedServiceCall
    {
        /// <summary>
        /// Numeric secure-channel id this call belongs to.
        /// </summary>
        public uint ChannelId { get; init; }

        /// <summary>
        /// Token id that secured this call.
        /// </summary>
        public uint TokenId { get; init; }

        /// <summary>
        /// Request id assigned by the client.
        /// </summary>
        public uint RequestId { get; init; }

        /// <summary>
        /// Capture timestamp of the first request chunk.
        /// </summary>
        public DateTimeOffset RequestTimestamp { get; init; }

        /// <summary>
        /// Capture timestamp of the last response chunk, if observed.
        /// </summary>
        public DateTimeOffset? ResponseTimestamp { get; init; }

        /// <summary>
        /// Service name (e.g. <c>ReadRequest</c>,
        /// <c>OpenSecureChannelRequest</c>). <c>null</c> when the request
        /// was not decoded.
        /// </summary>
        public string? RequestName { get; init; }

        /// <summary>
        /// Service name of the response. <c>null</c> when the response was
        /// not decoded.
        /// </summary>
        public string? ResponseName { get; init; }

        /// <summary>
        /// Status code reported in the response header (if available).
        /// </summary>
        public StatusCode? ResponseStatus { get; init; }

        /// <summary>
        /// Size of the request body in bytes (all chunks summed).
        /// </summary>
        public int RequestBodySize { get; init; }

        /// <summary>
        /// Size of the response body in bytes.
        /// </summary>
        public int ResponseBodySize { get; init; }

        /// <summary>
        /// Optional one-line summary of the request body (e.g.
        /// "ReadRequest 3 nodes, max age 0"). Implementations should keep
        /// this short - it is intended for human reading.
        /// </summary>
        public string? RequestSummary { get; init; }

        /// <summary>
        /// Optional one-line summary of the response body.
        /// </summary>
        public string? ResponseSummary { get; init; }

        /// <summary>
        /// Latency between request and response. <c>null</c> when the
        /// response was not observed.
        /// </summary>
        public TimeSpan? Latency => ResponseTimestamp - RequestTimestamp;

        /// <summary>
        /// Decoded annotations (key/value) attached to the call. Used by
        /// formatters that want to show extra detail without breaking the
        /// model.
        /// </summary>
        public IReadOnlyDictionary<string, string?> Annotations { get; init; }
            = new Dictionary<string, string?>();
    }
}
