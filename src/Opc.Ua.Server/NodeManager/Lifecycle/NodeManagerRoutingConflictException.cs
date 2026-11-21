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
            RoutingRevision = routingRevision;
        }

        /// <summary>
        /// Binds a host conflict to its first owning batch without relabeling nested batch failures.
        /// </summary>
        internal NodeManagerRoutingConflictException(
            NodeManagerRoutingConflictException failure,
            IPreparedNodeManagerBatch batch)
            : base(failure.Message, failure)
        {
            RoutingRevision = failure.RoutingRevision;
            Batch = batch;
        }

        /// <summary>
        /// Gets the batch whose own routing check failed, or null until that batch binds the host conflict.
        /// </summary>
        internal IPreparedNodeManagerBatch? Batch { get; }

        /// <summary>
        /// Gets the captured routing view checked by the publication host, or null for an unclassified failure.
        /// </summary>
        internal NodeManagerRoutingTable.RoutingSnapshot? RoutingRevision { get; }
    }
}
