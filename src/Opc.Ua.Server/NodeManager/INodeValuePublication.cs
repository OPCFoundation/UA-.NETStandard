// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Publishes final values of owned node subtrees before an application removes them.
    /// </summary>
    /// <remarks>
    /// The application must stop changing the subtrees before calling this interface and keep them
    /// unchanged through removal. Completion means inclusion in real Publish responses, not transport
    /// delivery or client acknowledgement. The operation never deletes nodes or generates Publish requests.
    /// </remarks>
    public interface INodeValuePublication
    {
        /// <summary>
        /// Samples the Value attributes of the subtrees for their active reporting monitored items and
        /// waits for those samples to enter Publish responses, with a thirty-second deadline.
        /// </summary>
        /// <param name="nodes">Registered roots owned by this node manager; descendants are included.</param>
        /// <param name="cancellationToken">Cancels observation without removing the nodes.</param>
        ValueTask WaitForValuePublicationAsync(
            ArrayOf<NodeState> nodes,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Samples the Value attributes of the subtrees for their active reporting monitored items and
        /// waits for those samples to enter Publish responses within the given deadline.
        /// </summary>
        /// <param name="nodes">Registered roots owned by this node manager; descendants are included.</param>
        /// <param name="timeout">A positive, finite deadline covering capture, sampling, and publication.</param>
        /// <param name="cancellationToken">Cancels observation without removing the nodes.</param>
        /// <remarks>
        /// Membership is captured at admission. Disabled and Sampling items, event items, non-Value attributes,
        /// and subscriptions without a live session are excluded. A captured item leaving Reporting or losing
        /// its session releases its obligation. Disabling publishing does not release it. Unsupported
        /// monitored items fail rather than being omitted. On any failure the caller must retain the nodes.
        /// </remarks>
        ValueTask WaitForValuePublicationAsync(
            ArrayOf<NodeState> nodes,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);
    }
}
