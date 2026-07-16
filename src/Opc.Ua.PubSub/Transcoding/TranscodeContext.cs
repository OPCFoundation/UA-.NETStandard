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
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Per-run environment shared by every stage of a transcode
    /// (transforms, profile projection, encode / decode, security).
    /// Bundles the encode/decode context, the metadata registry used to
    /// rehydrate RawData / Variant fields, and the telemetry context used
    /// to create loggers.
    /// </summary>
    /// <remarks>
    /// Holds no per-message state; one instance can safely be shared
    /// across every transcode driven by the same
    /// <c>PubSubTranscodingBridge</c> or standalone transcoder.
    /// </remarks>
    public sealed class TranscodeContext
    {
        /// <summary>
        /// Initializes a new <see cref="TranscodeContext"/>.
        /// </summary>
        /// <param name="encodingContext">
        /// Encode / decode environment forwarded to the pluggable
        /// encoders and decoders.
        /// </param>
        /// <param name="telemetry">
        /// Telemetry context used to create per-stage loggers.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when a required dependency is <see langword="null"/>.
        /// </exception>
        public TranscodeContext(
            PubSubNetworkMessageContext encodingContext,
            ITelemetryContext telemetry)
        {
            EncodingContext = encodingContext
                ?? throw new ArgumentNullException(nameof(encodingContext));
            Telemetry = telemetry
                ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Encode / decode environment (stack message context, metadata
        /// registry, diagnostics, clock) passed to the pluggable
        /// encoders and decoders.
        /// </summary>
        public PubSubNetworkMessageContext EncodingContext { get; }

        /// <summary>
        /// Telemetry context used to create per-stage loggers.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Shared metadata registry used to resolve
        /// <see cref="DataSetMetaDataType"/> when converting between
        /// field encodings that depend on metadata (RawData).
        /// </summary>
        public IDataSetMetaDataRegistry MetaDataRegistry
            => EncodingContext.MetaDataRegistry;
    }
}
