/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The master node manager for the server.
    /// </summary>
    public interface IMasterNodeManager
    {
        /// <summary>
        /// The node managers being managed.
        /// </summary>
        IReadOnlyList<IAsyncNodeManager> AsyncNodeManagers { get; }

        /// <summary>
        /// Returns the configuration node manager.
        /// </summary>
        IConfigurationNodeManager ConfigurationNodeManager { get; }

        /// <summary>
        /// Returns the core node manager.
        /// </summary>
        ICoreNodeManager CoreNodeManager { get; }

        /// <summary>
        /// Returns the diagnostics node manager.
        /// </summary>
        IDiagnosticsNodeManager DiagnosticsNodeManager { get; }

        /// <summary>
        /// The node managers being managed.
        /// </summary>
        IReadOnlyList<INodeManager> NodeManagers { get; }

        /// <summary>
        /// Adds the references to the target.
        /// </summary>
        ValueTask AddReferencesAsync(NodeId sourceId, IList<IReference> references, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the set of references that meet the filter criteria.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        ValueTask<(BrowseResultCollection results, DiagnosticInfoCollection diagnosticInfos)> BrowseAsync(
            OperationContext context,
            ViewDescription view,
            uint maxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Continues a browse operation that was previously halted.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        ValueTask<(BrowseResultCollection results, DiagnosticInfoCollection diagnosticInfos)> BrowseNextAsync(
            OperationContext context,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Calls a method defined on an object.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> is <c>null</c>.</exception>
        ValueTask<(CallMethodResultCollection results, DiagnosticInfoCollection diagnosticInfos)> CallAsync(
            OperationContext context,
            CallMethodRequestCollection methodsToCall,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Handles condition refresh request.
        /// </summary>
        ValueTask ConditionRefreshAsync(OperationContext context, IList<IEventMonitoredItem> monitoredItems, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        ValueTask CreateMonitoredItemsAsync(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            IList<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        ValueTask DeleteMonitoredItemsAsync(
            OperationContext context,
            uint subscriptionId,
            IList<IMonitoredItem> itemsToDelete,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the references to the target.
        /// </summary>
        ValueTask DeleteReferencesAsync(NodeId targetId, IList<IReference> references, CancellationToken cancellationToken = default);

        /// <summary>
        /// Searches the node id in all node managers,
        /// returns the node state if found (and node Manager supports it), otherwise returns null.
        /// </summary>
        ValueTask<NodeState> FindNodeInAddressSpaceAsync(NodeId nodeId);

        /// <summary>
        /// Returns node handle and its node manager.
        /// </summary>
        [Obsolete("Use GetManagerHandleAsync instead.")]
        object GetManagerHandle(NodeId nodeId, out IAsyncNodeManager nodeManager);

        /// <summary>
        /// Returns node handle and its node manager.
        /// </summary>
        object GetManagerHandle(NodeId nodeId, out INodeManager nodeManager);

        /// <summary>
        /// Returns node handle and its node manager.
        /// </summary>
        ValueTask<(object handle, IAsyncNodeManager nodeManager)> GetManagerHandleAsync(NodeId nodeId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the history of a set of items.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        ValueTask<(HistoryReadResultCollection values, DiagnosticInfoCollection diagnosticInfos)> HistoryReadAsync(
            OperationContext context,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the history for a set of nodes.
        /// </summary>
        ValueTask<(HistoryUpdateResultCollection results, DiagnosticInfoCollection diagnosticInfos)> HistoryUpdateAsync(
            OperationContext context,
            ExtensionObjectCollection historyUpdateDetails,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Modifies a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        ValueTask ModifyMonitoredItemsAsync(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            IList<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads a set of nodes.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="nodesToRead"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        ValueTask<(DataValueCollection values, DiagnosticInfoCollection diagnosticInfos)> ReadAsync(
            OperationContext context,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Registers the node manager as the node manager for Nodes in the specified namespace.
        /// </summary>
        /// <param name="namespaceUri">The URI of the namespace.</param>
        /// <param name="nodeManager">The NodeManager which owns node in the namespace.</param>
        /// <remarks>
        /// <para>
        /// Multiple NodeManagers may register interest in a Namespace.
        /// The order in which this method is called determines the precedence if multiple NodeManagers exist.
        /// This method adds the namespaceUri to the Server's Namespace table if it does not already exist.
        /// </para>
        /// <para>This method is thread safe and can be called at anytime.</para>
        /// <para>
        /// This method does not have to be called for any namespaces that were in the NodeManager's
        /// NamespaceUri property when the MasterNodeManager was created.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Throw if the namespaceUri or the nodeManager are null.</exception>
        void RegisterNamespaceManager(string namespaceUri, IAsyncNodeManager nodeManager);

        /// <summary>
        /// Registers the node manager as the node manager for Nodes in the specified namespace.
        /// </summary>
        /// <param name="namespaceUri">The URI of the namespace.</param>
        /// <param name="nodeManager">The NodeManager which owns node in the namespace.</param>
        /// <remarks>
        /// <para>
        /// Multiple NodeManagers may register interest in a Namespace.
        /// The order in which this method is called determines the precedence if multiple NodeManagers exist.
        /// This method adds the namespaceUri to the Server's Namespace table if it does not already exist.
        /// </para>
        /// <para>This method is thread safe and can be called at anytime.</para>
        /// <para>
        /// This method does not have to be called for any namespaces that were in the NodeManager's
        /// NamespaceUri property when the MasterNodeManager was created.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Throw if the namespaceUri or the nodeManager are null.</exception>
        void RegisterNamespaceManager(string namespaceUri, INodeManager nodeManager);

        /// <summary>
        /// Registers a set of node ids.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="nodesToRegister"/> is <c>null</c>.</exception>
        void RegisterNodes(OperationContext context, NodeIdCollection nodesToRegister, out NodeIdCollection registeredNodeIds);

        /// <summary>
        /// Deletes the specified references.
        /// </summary>
        void RemoveReferences(List<LocalReference> referencesToRemove);

        /// <summary>
        /// Deletes the specified references.
        /// </summary>
        ValueTask RemoveReferencesAsync(List<LocalReference> referencesToRemove, CancellationToken cancellationToken = default);

        /// <summary>
        /// Restore a set of monitored items after a Server Restart.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="itemsToRestore"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        ValueTask RestoreMonitoredItemsAsync(
            IList<IStoredMonitoredItem> itemsToRestore,
            IList<IMonitoredItem> monitoredItems,
            IUserIdentity savedOwnerIdentity,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Signals that a session is closing.
        /// </summary>
        ValueTask SessionClosingAsync(OperationContext context, NodeId sessionId, bool deleteSubscriptions, CancellationToken cancellationToken = default);

        /// <summary>
        /// Changes the monitoring mode for a set of items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        ValueTask SetMonitoringModeAsync(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> itemsToModify,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Shuts down the node managers.
        /// </summary>
        ValueTask ShutdownAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the node managers and start them
        /// </summary>
        ValueTask StartupAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Transfers a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        ValueTask TransferMonitoredItemsAsync(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Translates a start node id plus a relative paths into a node id.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="browsePaths"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        ValueTask<(BrowsePathResultCollection results, DiagnosticInfoCollection diagnosticInfos)> TranslateBrowsePathsToNodeIdsAsync(
            OperationContext context,
            BrowsePathCollection browsePaths,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Unregisters the node manager as the node manager for Nodes in the specified namespace.
        /// </summary>
        /// <param name="namespaceUri">The URI of the namespace.</param>
        /// <param name="nodeManager">The NodeManager which no longer owns nodes in the namespace.</param>
        /// <returns>A value indicating whether the node manager was successfully unregistered.</returns>
        /// <exception cref="ArgumentNullException">Throw if the namespaceUri or the nodeManager are null.</exception>
        bool UnregisterNamespaceManager(string namespaceUri, IAsyncNodeManager nodeManager);

        /// <summary>
        /// Unregisters the node manager as the node manager for Nodes in the specified namespace.
        /// </summary>
        /// <param name="namespaceUri">The URI of the namespace.</param>
        /// <param name="nodeManager">The NodeManager which no longer owns nodes in the namespace.</param>
        /// <returns>A value indicating whether the node manager was successfully unregistered.</returns>
        /// <exception cref="ArgumentNullException">Throw if the namespaceUri or the nodeManager are null.</exception>
        bool UnregisterNamespaceManager(string namespaceUri, INodeManager nodeManager);

        /// <summary>
        /// Unregisters a set of node ids.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="nodesToUnregister"/> is <c>null</c>.</exception>
        void UnregisterNodes(OperationContext context, NodeIdCollection nodesToUnregister);

        /// <summary>
        /// Writes a set of values.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        ValueTask<(StatusCodeCollection results, DiagnosticInfoCollection diagnosticInfos)> WriteAsync(
            OperationContext context,
            WriteValueCollection nodesToWrite,
            CancellationToken cancellationToken = default);
    }
}
