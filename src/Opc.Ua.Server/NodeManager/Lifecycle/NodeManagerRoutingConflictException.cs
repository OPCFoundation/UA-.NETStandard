// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Identifies a captured routing view superseded before a batch's durable decision.
    /// </summary>
    internal sealed class NodeManagerRoutingConflictException : InvalidOperationException
    {
        /// <summary>
        /// Creates a conflict for a superseded routing view.
        /// </summary>
        public NodeManagerRoutingConflictException()
            : base("The captured routing view has changed.")
        {
        }

        /// <summary>
        /// Creates a routing-view conflict with its preparation-stage diagnostic.
        /// </summary>
        public NodeManagerRoutingConflictException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates a routing-view conflict with its originating failure.
        /// </summary>
        public NodeManagerRoutingConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates a conflict identifying the exact captured routing view checked by the publication host.
        /// </summary>
        internal NodeManagerRoutingConflictException(
            string message,
            NodeManagerRoutingTable.RoutingSnapshot routingRevision)
            : base(message)
        {
            m_routingRevision = new WeakReference<NodeManagerRoutingTable.RoutingSnapshot>(
                routingRevision ?? throw new ArgumentNullException(nameof(routingRevision)));
        }

        /// <summary>
        /// Gets the live captured routing view checked by the publication host, if available.
        /// </summary>
        internal NodeManagerRoutingTable.RoutingSnapshot? RoutingRevision =>
            m_routingRevision is not null &&
            m_routingRevision.TryGetTarget(out NodeManagerRoutingTable.RoutingSnapshot? routingRevision)
                ? routingRevision
                : null;

        /// <summary>The captured routing view's non-owning identity.</summary>
        private readonly WeakReference<NodeManagerRoutingTable.RoutingSnapshot>? m_routingRevision;
    }
}
