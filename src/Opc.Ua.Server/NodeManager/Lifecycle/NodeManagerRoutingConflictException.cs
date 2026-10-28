/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
