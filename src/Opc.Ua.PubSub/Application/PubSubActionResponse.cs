// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Requester-side PubSub Action response.
    /// </summary>
    public sealed record PubSubActionResponse
    {
        /// <summary>
        /// Target that produced the response.
        /// </summary>
        public PubSubActionTarget Target { get; init; } = new();

        /// <summary>
        /// RequestId copied from the matching request.
        /// </summary>
        public ushort RequestId { get; init; }

        /// <summary>
        /// Correlation data copied from the matching request.
        /// </summary>
        public ByteString CorrelationData { get; init; } = ByteString.Empty;

        /// <summary>
        /// Action execution status.
        /// </summary>
        public StatusCode StatusCode { get; init; } = StatusCodes.Good;

        /// <summary>
        /// Action lifecycle state reported by the responder.
        /// </summary>
        public ActionState ActionState { get; init; } = ActionState.Done;

        /// <summary>
        /// Named output fields returned by the Action handler.
        /// </summary>
        public ArrayOf<DataSetField> OutputFields { get; init; } = [];
    }
}
