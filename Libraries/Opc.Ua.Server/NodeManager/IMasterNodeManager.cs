/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Interface of the master node manager.
    /// </summary>
    public interface IMasterNodeManager : IDisposable
    {
        /// <summary>
        /// Returns the core node manager.
        /// </summary>
        ICoreNodeManager CoreNodeManager { get; }

        /// <summary>
        /// Returns the diagnostics node manager.
        /// </summary>
        IDiagnosticsNodeManager DiagnosticsNodeManager { get; }

        /// <summary>
        /// Returns the configuration node manager.
        /// </summary>
        IConfigurationNodeManager ConfigurationNodeManager { get; }

        /// <summary>
        /// The node managers being managed.
        /// </summary>
        IList<INodeManager> NodeManagers { get; }

        /// <summary>
        /// Creates the node managers and start them
        /// </summary>
        void Startup();

        /// <summary>
        /// Signals that a session is closing.
        /// </summary>
        void SessionClosing(OperationContext context, NodeId sessionId, bool deleteSubscriptions);

        /// <summary>
        /// Shuts down the node managers.
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Registers the node manager as the node manager for Nodes in the specified namespace.
        /// </summary>
        /// <param name="namespaceUri">The URI of the namespace.</param>
        /// <param name="nodeManager">The NodeManager which owns node in the namespace.</param>
        /// <remarks>
        /// Multiple NodeManagers may register interest in a Namespace.
        /// The order in which this method is called determines the precedence if multiple NodeManagers exist.
        /// This method adds the namespaceUri to the Server's Namespace table if it does not already exist.
        ///
        /// This method is thread safe and can be called at anytime.
        ///
        /// This method does not have to be called for any namespaces that were in the NodeManager's
        /// NamespaceUri property when the MasterNodeManager was created.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Throw if the namespaceUri or the nodeManager are null.</exception>
        void RegisterNamespaceManager(string namespaceUri, INodeManager nodeManager);

        /// <summary>
        /// Unregisters the node manager as the node manager for Nodes in the specified namespace.
        /// </summary>
        /// <param name="namespaceUri">The URI of the namespace.</param>
        /// <param name="nodeManager">The NodeManager which no longer owns nodes in the namespace.</param>
        /// <returns>A value indicating whether the node manager was successfully unregistered.</returns>
        /// <exception cref="ArgumentNullException">Throw if the namespaceUri or the nodeManager are null.</exception>
        bool UnregisterNamespaceManager(string namespaceUri, INodeManager nodeManager);

        /// <summary>
        /// Returns node handle and its node manager.
        /// </summary>
        object GetManagerHandle(NodeId nodeId, out INodeManager nodeManager);

        /// <summary>
        /// Adds the references to the target.
        /// </summary>
        void AddReferences(NodeId sourceId, IList<IReference> references);

        /// <summary>
        /// Deletes the references to the target.
        /// </summary>
        void DeleteReferences(NodeId targetId, IList<IReference> references);

        /// <summary>
        /// Deletes the specified references.
        /// </summary>
        void RemoveReferences(List<LocalReference> referencesToRemove);

        /// <summary>
        /// Registers a set of node ids.
        /// </summary>
        void RegisterNodes(
            OperationContext context,
            NodeIdCollection nodesToRegister,
            out NodeIdCollection registeredNodeIds);

        /// <summary>
        /// Unregisters a set of node ids.
        /// </summary>
        void UnregisterNodes(
            OperationContext context,
            NodeIdCollection nodesToUnregister);

        /// <summary>
        /// Translates a start node id plus a relative paths into a node id.
        /// </summary>
        void TranslateBrowsePathsToNodeIds(
            OperationContext context,
            BrowsePathCollection browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Returns the set of references that meet the filter criteria.
        /// </summary>
        void Browse(
            OperationContext context,
            ViewDescription view,
            uint maxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Continues a browse operation that was previously halted.
        /// </summary>
        void BrowseNext(
            OperationContext context,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Reads a set of nodes.
        /// </summary>
        void Read(
            OperationContext context,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            out DataValueCollection values,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Reads the history of a set of items.
        /// </summary>
        void HistoryRead(
            OperationContext context,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Writes a set of values.
        /// </summary>
        void Write(
            OperationContext context,
            WriteValueCollection nodesToWrite,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Updates the history for a set of nodes.
        /// </summary>
        void HistoryUpdate(
            OperationContext context,
            ExtensionObjectCollection historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Calls a method defined on an object.
        /// </summary>
        void Call(
            OperationContext context,
            CallMethodRequestCollection methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Handles condition refresh request.
        /// </summary>
        void ConditionRefresh(OperationContext context, IList<IEventMonitoredItem> monitoredItems);

        /// <summary>
        /// Creates a set of monitored items.
        /// </summary>
        void CreateMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            IList<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable);

        /// <summary>
        /// Modifies a set of monitored items.
        /// </summary>
        void ModifyMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            IList<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults);

        /// <summary>
        /// Transfers a set of monitored items.
        /// </summary>
        void TransferMonitoredItems(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<ServiceResult> errors);

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        void DeleteMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            IList<IMonitoredItem> itemsToDelete,
            IList<ServiceResult> errors);

        /// <summary>
        /// Changes the monitoring mode for a set of items.
        /// </summary>
        void SetMonitoringMode(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> itemsToModify,
            IList<ServiceResult> errors);
    }
}
