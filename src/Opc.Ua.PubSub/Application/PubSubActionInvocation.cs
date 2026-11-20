// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Responder-side Action invocation supplied to an Action handler.
    /// </summary>
    public sealed record PubSubActionInvocation
    {
        /// <summary>
        /// Target DataSetWriter and Action target.
        /// </summary>
        public PubSubActionTarget Target { get; init; } = new();

        /// <summary>
        /// RequestId supplied by the requester.
        /// </summary>
        public ushort RequestId { get; init; }

        /// <summary>
        /// Correlation data supplied by the requester.
        /// </summary>
        public ByteString CorrelationData { get; init; } = ByteString.Empty;

        /// <summary>
        /// Input fields supplied by the requester.
        /// </summary>
        public ArrayOf<DataSetField> InputFields { get; init; } = [];

        /// <summary>
        /// Response address supplied by the requester.
        /// </summary>
        public string ResponseAddress { get; init; } = string.Empty;

        /// <summary>
        /// Timeout hint supplied by the requester in milliseconds.
        /// </summary>
        public double TimeoutHint { get; init; }
    }
}
