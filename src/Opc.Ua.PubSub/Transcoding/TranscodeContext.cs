// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
