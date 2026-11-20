// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client
{
    /// <summary>
    /// Options for OPC 10000-4 §6.6.3 client redundancy Subscription takeover with <c>TransferSubscriptions</c>.
    /// </summary>
    public sealed record ClientRedundancyTransferOptions
    {
        /// <summary>
        /// Sends initial values after the backup client takes ownership of transferred subscriptions.
        /// </summary>
        public bool SendInitialValues { get; init; } = true;

        /// <summary>
        /// The active client's session id. If omitted, <see cref="ActiveSessionName"/> is used.
        /// </summary>
        public NodeId ActiveSessionId { get; init; } = NodeId.Null;

        /// <summary>
        /// The active client's session name used to find its session diagnostics.
        /// The backup session is excluded. If multiple other sessions share this
        /// name, discovery fails; specify <see cref="ActiveSessionId"/> instead.
        /// </summary>
        public string ActiveSessionName { get; init; } = string.Empty;

        /// <summary>
        /// Expected active-client user display name. When set, the backup session must use the same user.
        /// </summary>
        public string ActiveUserDisplayName { get; init; } = string.Empty;
    }
}
