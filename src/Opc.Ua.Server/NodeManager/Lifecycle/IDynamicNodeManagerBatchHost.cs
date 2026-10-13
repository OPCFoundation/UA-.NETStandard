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
    /// Optional host support for a single prepared routing publication.
    /// </summary>
    internal interface IDynamicNodeManagerBatchHost
    {
        /// <summary>
        /// Gets the current live routing image whose identity is the preparation revision.
        /// </summary>
        NodeManagerRoutingTable.RoutingSnapshot RoutingRevision { get; }

        /// <summary>
        /// Uses live routing during lifecycle work instead of an enclosing Client request's captured image.
        /// </summary>
        /// <returns>A scope that restores the previous routing when disposed.</returns>
        IDisposable UseLiveRouting();

        /// <summary>
        /// Uses a private type image while preparing or discarding a candidate.
        /// </summary>
        /// <param name="typeTree">The private type tree to use.</param>
        /// <param name="factory">The private encodeable factory to use.</param>
        /// <returns>A scope that restores the previous type image when disposed.</returns>
        IDisposable UseTypeImage(TypeTable typeTree, EncodeableFactory factory);

        /// <summary>
        /// Releases the current binding admission while a callback waits for and executes lifecycle work.
        /// Async disposal resumes admission after lifecycle serialization has been released.
        /// </summary>
        /// <returns>A suspension that resumes admission when disposed asynchronously.</returns>
        IAsyncDisposable SuspendBindingAdmission();

        /// <summary>
        /// Releases the binding admission captured for the specified request context, or the ambient admission of
        /// a legacy caller when the context is null or was not admitted.
        /// </summary>
        /// <param name="context">The operation context the admitted request was started with, or null.</param>
        /// <returns>A suspension that resumes admission when disposed asynchronously.</returns>
        IAsyncDisposable SuspendBindingAdmission(IOperationContext? context);

        /// <summary>
        /// Checks whether the admitted operation is already removing this exact monitored item.
        /// </summary>
        /// <param name="monitoredItem">The monitored item to check.</param>
        /// <returns><c>true</c> if the admitted operation is removing the item.</returns>
        bool IsRemovingBinding(IMonitoredItem monitoredItem);

        /// <summary>
        /// Checks whether the request admitted for the context, or the ambient admission of a legacy caller, is
        /// already removing this exact monitored item.
        /// </summary>
        /// <param name="context">The operation context the admitted request was started with, or null.</param>
        /// <param name="monitoredItem">The monitored item to check.</param>
        /// <returns><c>true</c> if the admitted operation is removing the item.</returns>
        bool IsRemovingBinding(IOperationContext? context, IMonitoredItem monitoredItem);

        /// <summary>
        /// Dispatches Session activation and its notifications against the published bindings.
        /// </summary>
        /// <param name="context">The ActivateSession request context, or null for an independent caller.</param>
        /// <param name="activateAsync">The callback performing the Session activation.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The server nonce and activation status produced by <paramref name="activateAsync"/>.</returns>
        ValueTask<(ByteString ServerNonce, ServiceResult ActivationStatus)> DispatchSessionActivationAsync(
            IOperationContext? context,
            Func<ValueTask<(ByteString ServerNonce, ServiceResult ActivationStatus)>> activateAsync,
            CancellationToken cancellationToken);

        /// <summary>
        /// Reserves and checks the complete candidate, then publishes it when a decision is supplied.
        /// A null decision validates and releases the reservations without changing the serving image.
        /// </summary>
        /// <param name="candidates">The prepared node managers to add or replace.</param>
        /// <param name="removed">The node managers removed by the batch.</param>
        /// <param name="immediateRetirements">The removed managers that retire without draining.</param>
        /// <param name="routingRevision">The routing image the batch was prepared against.</param>
        /// <param name="typeTree">The candidate type tree.</param>
        /// <param name="originalTypes">The type tree the batch was prepared against.</param>
        /// <param name="typeRevision">The revision of the type tree the batch was prepared against.</param>
        /// <param name="factory">The candidate encodeable factory.</param>
        /// <param name="originalFactory">The encodeable factory the batch was prepared against.</param>
        /// <param name="factoryRevision">The revision of the factory the batch was prepared against.</param>
        /// <param name="readImages">The read images to bind when the batch is published.</param>
        /// <param name="decideAsync">The callback deciding whether to publish, or null to validate only.</param>
        /// <param name="published">The callback invoked after the candidate has been published.</param>
        /// <param name="reconcileBindingsAsync">The callback reconciling monitored item bindings.</param>
        /// <param name="reportCleanupFailure">The callback receiving cleanup failures.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>A task that completes when the batch has been validated or published.</returns>
        ValueTask CommitBatchAsync(
            ArrayOf<PreparedNodeManager> candidates,
            ArrayOf<IAsyncNodeManager> removed,
            ArrayOf<IAsyncNodeManager> immediateRetirements,
            NodeManagerRoutingTable.RoutingSnapshot routingRevision,
            TypeTable typeTree,
            TypeTable originalTypes,
            long typeRevision,
            EncodeableFactory factory,
            EncodeableFactory originalFactory,
            long factoryRevision,
            ArrayOf<INodeManagerReadImage> readImages,
            Func<CancellationToken, ValueTask>? decideAsync,
            Action published,
            Func<ValueTask> reconcileBindingsAsync,
            Action<Exception> reportCleanupFailure,
            CancellationToken cancellationToken);
    }
}
