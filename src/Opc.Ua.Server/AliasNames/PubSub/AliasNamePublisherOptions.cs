// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Opc.Ua.Server.AliasNames.PubSub
{
    /// <summary>
    /// Tunables for <see cref="AliasNamePublisher"/>.
    /// </summary>
    public sealed class AliasNamePublisherOptions
    {
        /// <summary>
        /// The application URI stamped into the
        /// <see cref="AliasUpdateDataType.ApplicationUri"/> field of
        /// every produced message. Defaults to the server's
        /// application URI when wired via the
        /// <see cref="AliasNamePublisher(IAliasNameStoreRegistry, IPortableNodeIdResolver, string, AliasNamePublisherOptions)"/>
        /// constructor.
        /// </summary>
        public string ApplicationUri { get; set; } = string.Empty;

        /// <summary>
        /// Filter — when non-<c>null</c>, only category changes whose
        /// <see cref="NodeId"/> is in this set produce messages.
        /// Defaults to <c>null</c> (every change is forwarded).
        /// </summary>
        public ISet<NodeId>? IncludedCategories { get; set; }
    }
}
