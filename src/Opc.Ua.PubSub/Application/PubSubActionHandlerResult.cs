// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Result returned by a PubSub Action handler.
    /// </summary>
    public sealed record PubSubActionHandlerResult
    {
        /// <summary>
        /// Action execution status.
        /// </summary>
        public StatusCode StatusCode { get; init; } = StatusCodes.Good;

        /// <summary>
        /// Named output fields to include in the Action response.
        /// </summary>
        public ArrayOf<DataSetField> OutputFields { get; init; } = [];
    }
}
