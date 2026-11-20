/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Versioned opaque payload for a portable HistoryRead continuation point.
    /// </summary>
    public sealed record HistoryContinuationPointEnvelope
    {
        /// <summary>
        /// Continuation point identifier returned to the client.
        /// </summary>
        public Guid Id { get; init; }

        /// <summary>
        /// Session that originally owned the continuation point.
        /// </summary>
        public NodeId OwnerSessionId { get; init; }

        /// <summary>
        /// Codec identifier understood by the server that created the payload.
        /// </summary>
        public string CodecId { get; init; } = string.Empty;

        /// <summary>
        /// Codec-specific payload version.
        /// </summary>
        public uint CodecVersion { get; init; }

        /// <summary>
        /// Protected codec payload.
        /// </summary>
        public ByteString Payload { get; init; }
    }
}
