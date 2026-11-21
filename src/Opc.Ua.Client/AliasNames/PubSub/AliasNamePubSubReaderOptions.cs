// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Client.AliasNames.PubSub
{
    /// <summary>
    /// Tunables for <see cref="AliasNamePubSubReader"/>.
    /// </summary>
    public sealed class AliasNamePubSubReaderOptions
    {
        /// <summary>
        /// When non-<c>null</c>, only messages whose
        /// <see cref="AliasUpdateDataType.ApplicationUri"/> equals this
        /// string are forwarded. Useful when the same DataSet is
        /// shared by multiple publishers.
        /// </summary>
        public string? ExpectedApplicationUri { get; set; }
    }
}
