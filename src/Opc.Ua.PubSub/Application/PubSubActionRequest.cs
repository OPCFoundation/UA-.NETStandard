// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Requester-side PubSub Action invocation options.
    /// </summary>
    public sealed record PubSubActionRequest
    {
        /// <summary>
        /// Target DataSetWriter and Action target.
        /// </summary>
        public PubSubActionTarget Target { get; init; } = new();

        /// <summary>
        /// Named input fields passed to the Action handler.
        /// </summary>
        public ArrayOf<DataSetField> InputFields { get; init; } = [];

        /// <summary>
        /// Optional response address carried on the request.
        /// </summary>
        public string ResponseAddress { get; init; } = string.Empty;

        /// <summary>
        /// Optional processing timeout hint in milliseconds.
        /// </summary>
        public double TimeoutHint { get; init; }
    }
}
