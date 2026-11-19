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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Performs awaited initialization that requires a published address space and
    /// initialized server subsystems, including registration of dependent NodeManagers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// StandardServer invokes this optional participant for its initial NodeManagers.
    /// The lifecycle invokes it for each newly committed Add or reload generation.
    /// Both await completion before returning to their caller. Custom hosts must invoke
    /// it after address-space creation and server initialization, not during preparation.
    /// </para>
    /// <para>
    /// Runtime readiness runs outside registration serialization. The generation remains
    /// published and owned by its startup operation: competing removal or reload is
    /// rejected until that operation completes. Runtime publication invokes readiness with
    /// a non-cancelable token; canceling the original registration request cannot abandon
    /// committed initialization. A readiness failure is a post-commit failure, not a rollback.
    /// The live registration remains available
    /// for recovery or removal. DeleteAddressSpaceAsync must release any dependencies
    /// created before the failure.
    /// </para>
    /// </remarks>
    public interface INodeManagerReadinessNotification
    {
        /// <summary>
        /// Completes initialization once runtime lifecycle operations are available.
        /// </summary>
        /// <param name="cancellationToken">
        /// The host's startup cancellation token, or a non-cancelable token for committed runtime publication.
        /// </param>
        ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Initializes a published generation using explicit, revocable readiness admission.
    /// </summary>
    public interface INodeManagerReadinessInitializer
    {
        /// <summary>
        /// Completes initialization before ordinary requests can dispatch to this generation.
        /// </summary>
        /// <param name="readiness">The capability for the exact server and generation.</param>
        /// <param name="cancellationToken">The host's readiness cancellation token.</param>
        ValueTask OnServerReadyAsync(
            NodeManagerReadinessContext readiness,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Carries readiness admission without selecting or retaining a request routing image.
    /// </summary>
    public sealed class NodeManagerReadinessContext
    {
        internal NodeManagerReadinessContext(NodeManagerReadinessScope scope)
        {
            Scope = scope;
        }

        /// <summary>
        /// Reads nodes through the normal server request path, including the initializer's pending generation.
        /// </summary>
        /// <param name="server">The local server receiving the read.</param>
        /// <param name="channel">The channel on which the session was authenticated.</param>
        /// <param name="header">The local request header.</param>
        /// <param name="maxAge">The maximum acceptable cache age, in milliseconds.</param>
        /// <param name="timestampsToReturn">The timestamps requested by the caller.</param>
        /// <param name="nodes">The node attributes to read.</param>
        /// <param name="requestLifetime">The lifetime of the read request.</param>
        /// <returns>The read response.</returns>
        public ValueTask<ReadResponse> ReadAsync(
            StandardServer server,
            SecureChannelContext channel,
            RequestHeader header,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ArrayOf<ReadValueId> nodes,
            RequestLifetime requestLifetime)
        {
            return (server ?? throw new ArgumentNullException(nameof(server))).ReadWithReadinessAsync(
                channel, header, maxAge, timestampsToReturn, nodes, requestLifetime, Scope);
        }

        /// <summary>
        /// Publishes dependent changes while the initializer is still active.
        /// </summary>
        /// <param name="server">The local server that owns the dependent changes.</param>
        /// <param name="changes">The additions, replacements, or retirements to publish.</param>
        /// <param name="cancellationToken">Cancels before the publication commits.</param>
        /// <returns>The committed publication result.</returns>
        public ValueTask<LifecycleResult> PublishAsync(
            StandardServer server,
            ArrayOf<LifecycleChange> changes,
            CancellationToken cancellationToken)
        {
            return (server ?? throw new ArgumentNullException(nameof(server))).PublishWithReadinessAsync(
                changes, Scope, cancellationToken);
        }

        /// <summary>
        /// Gets the scope that the lifecycle revokes on return, failure, or cancellation.
        /// </summary>
        internal NodeManagerReadinessScope Scope { get; }
    }

    /// <summary>
    /// Readiness participation helpers for adapted and directly implemented managers.
    /// </summary>
    internal static class NodeManagerReadiness
    {
        /// <summary>
        /// Gets whether a manager or its wrapped implementation has a readiness callback.
        /// </summary>
        /// <param name="nodeManager">The manager to inspect.</param>
        /// <returns>True when the manager has a callback that must complete before readiness is released.</returns>
        internal static bool IsParticipant(IAsyncNodeManager nodeManager)
        {
            return nodeManager switch
            {
                AsyncNodeManagerAdapter adapter => adapter.HasReadinessParticipant,
                _ => nodeManager is INodeManagerReadinessInitializer or INodeManagerReadinessNotification
            };
        }

        /// <summary>
        /// Finds the actual initializer, not an adapter that exposes an optional callback.
        /// </summary>
        internal static INodeManagerReadinessInitializer? FindInitializer(IAsyncNodeManager nodeManager)
        {
            return nodeManager switch
            {
                AsyncNodeManagerAdapter { SyncNodeManager: SyncNodeManagerAdapter sync } =>
                    FindInitializer(sync.AsyncNodeManager),
                AsyncNodeManagerAdapter adapter => adapter.SyncNodeManager as INodeManagerReadinessInitializer,
                INodeManagerReadinessInitializer initializer => initializer,
                _ => null
            };
        }
    }
}
