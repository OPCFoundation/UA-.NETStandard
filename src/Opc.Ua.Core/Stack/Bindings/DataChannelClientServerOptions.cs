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
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Finite reservations for an explicitly enabled native Client/Server lane.
    /// These limits are additional to the native context's application-wide budget.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed record DataChannelClientServerOptions
    {
        /// <summary>
        /// Gets the maximum complete UACP chunk size requested by a lane.
        /// Native record overhead is not included and negotiation may reduce this value.
        /// </summary>
        public int MaxChunkSize { get; init; } = 32768;

        /// <summary>
        /// Gets the per-direction encoded-byte receive reservation requested by a lane.
        /// </summary>
        public uint MaxReceiveBytes { get; init; } = 262144;

        /// <summary>
        /// Gets the maximum simultaneous lanes owned by one client or listener.
        /// </summary>
        public int MaxLanes { get; init; } = 16;

        /// <summary>
        /// Gets the maximum concurrently outstanding Service requests on one client lane.
        /// </summary>
        public int MaxPendingRequests { get; init; } = 128;

        /// <summary>
        /// Rejects unbounded reservations and chunks below the UACP minimum.
        /// </summary>
        internal void Validate()
        {
            if (MaxChunkSize is < TcpMessageLimits.MinBufferSize or > 16777216 ||
                MaxReceiveBytes < (uint)MaxChunkSize + 13 ||
                MaxLanes is <= 0 or > 128 || MaxPendingRequests <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DataChannelClientServerOptions), "Lane limits must be finite and admit a complete UACP chunk.");
            }
        }

        /// <summary>
        /// Creates the exact reserved lane descriptor without granting application rights.
        /// </summary>
        internal DataChannelParametersDataType CreateParameters(int bufferSize)
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = DataChannelProfiles.ClientServerResource,
                Protocol = DataChannelProfiles.OpaquePayload,
                ContentType = "application/octet-stream",
                Direction = DataChannelDirection.Duplex,
                Delivery = DataChannelDeliveryMode.Ordered,
                Protection = DataChannelProtectionMode.Confidential,
                AllowQueueExpiry = false,
                Priority = 1,
                MaxMessageSize = (uint)Math.Min(MaxChunkSize, bufferSize),
                MaxReceiveBytes = MaxReceiveBytes
            };
        }
    }
}
