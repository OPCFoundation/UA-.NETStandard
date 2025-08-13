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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An interface to an object that creates a INodeManager object.
    /// </summary>
    public interface INodeManagerFactory
    {
        /// <summary>
        /// The INodeManager factory.
        /// </summary>
        /// <param name="server">The server instance.</param>
        /// <param name="configuration">The application configuration.</param>
        INodeManager Create(IServerInternal server, ApplicationConfiguration configuration);

        /// <summary>
        /// The namespace table of the NodeManager.
        /// </summary>
        StringCollection NamespacesUris { get; }
    }

    /// <summary>
    /// An interface to an object that manages a set of nodes in the address space.
    /// </summary>
    public interface INodeManager
    {
        /// <summary>
        /// Returns the NamespaceUris for the Nodes belonging to the NodeManager.
        /// </summary>
        /// <remarks>
        /// <para>By default the MasterNodeManager uses the namespaceIndex to determine who owns an Node.</para>
        /// <para>
        /// Servers that do not wish to partition their address space this way must provide their own
        /// implementation of MasterNodeManager.GetManagerHandle().
        /// </para>
        /// <para>NodeManagers which depend on a custom partitioning scheme must return a null value.</para>
        /// </remarks>
        IEnumerable<string> NamespaceUris { get; }

        /// <summary>
        /// Creates the address space by loading any configuration information an connecting to an underlying system (if applicable).
        /// </summary>
        /// <returns>A table of references that need to be added to other node managers.</returns>
        /// <remarks>
        /// A node manager owns a set of nodes. These nodes may be known in advance or they may be stored in an
        /// external system are retrived on demand. These nodes may have two way references to nodes that are owned
        /// by other node managers. In these cases, the node managers only manage one half of those references. The
        /// other half of the reference should be returned to the MasterNodeManager.
        /// </remarks>
        void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences);

        /// <summary>
        /// Deletes the address by releasing all resources and disconnecting from any underlying system.
        /// </summary>
        void DeleteAddressSpace();

        /// <summary>
        /// Returns an opaque handle identifying to the node to the node manager.
        /// </summary>
        /// <returns>A node handle, null if the node manager does not recognize the node id.</returns>
        /// <remarks>
        /// The method must not block by querying an underlying system. If the node manager wraps an
        /// underlying system then it must check to see if it recognizes the syntax of the node id.
        /// The handle in this case may simply be a partially parsed version of the node id.
        /// </remarks>
        object GetManagerHandle(NodeId nodeId);

        /// <summary>
        /// Adds references to the node manager.
        /// </summary>
        /// <remarks>
        /// The node manager checks the dictionary for nodes that it owns and ensures the associated references exist.
        /// </remarks>
        void AddReferences(IDictionary<NodeId, IList<IReference>> references);

        /// <summary>
        /// Deletes a reference.
        /// </summary>
        ServiceResult DeleteReference(
            object sourceHandle,
            NodeId referenceTypeId,
            bool isInverse,
            ExpandedNodeId targetId,
            bool deleteBidirectional
        );

        /// <summary>
        /// Returns the metadata associated with the node.
        /// </summary>
        /// <remarks>
        /// Returns null if the node does not exist.
        /// </remarks>
        NodeMetadata GetNodeMetadata(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask);

        /// <summary>
        /// Returns the set of references that meet the filter criteria.
        /// </summary>
        /// <param name="context">The context to used when processing the request.</param>
        /// <param name="continuationPoint">The continuation point that stores the state of the Browse operation.</param>
        /// <param name="references">The list of references that meet the filter criteria.</param>
        /// <remarks>
        /// NodeManagers will likely have references to other NodeManagers which means they will not be able
        /// to apply the NodeClassMask or fill in the attributes for the target Node. In these cases the
        /// NodeManager must return a ReferenceDescription with the NodeId and ReferenceTypeId set. The caller will
        /// be responsible for filling in the target attributes.
        /// The references parameter may already contain references when the method is called. The implementer must
        /// include these references when calculating whether a continuation point must be returned.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if the context, continuationPoint or references parameters are null.</exception>
        /// <exception cref="ServiceResultException">Thrown if an error occurs during processing.</exception>
        void Browse(
            OperationContext context,
            ref ContinuationPoint continuationPoint,
            IList<ReferenceDescription> references
        );

        /// <summary>
        /// Finds the targets of the relative path from the source node.
        /// </summary>
        /// <param name="context">The context to used when processing the request.</param>
        /// <param name="sourceHandle">The handle for the source node.</param>
        /// <param name="relativePath">The relative path to follow.</param>
        /// <param name="targetIds">The NodeIds for any target at the end of the relative path.</param>
        /// <param name="unresolvedTargetIds">The NodeIds for any local target that is in another NodeManager.</param>
        /// <remarks>
        /// A null context indicates that the server's internal logic is making the call.
        /// The first target in the list must be the target that matches the instance declaration (if applicable).
        /// Any local targets that belong to other NodeManagers are returned as unresolvedTargetIds.
        /// The caller must check the BrowseName to determine if it matches the relativePath.
        /// The implementor must not throw an exception if the source or target nodes do not exist.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if the sourceHandle, relativePath or targetIds parameters are null.</exception>
        void TranslateBrowsePath(
            OperationContext context,
            object sourceHandle,
            RelativePathElement relativePath,
            IList<ExpandedNodeId> targetIds,
            IList<NodeId> unresolvedTargetIds
        );

        /// <summary>
        /// Reads the attribute values for a set of nodes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The MasterNodeManager pre-processes the nodesToRead and ensures that:
        ///    - the AttributeId is a known attribute.
        ///    - the IndexRange, if specified, is valid.
        ///    - the DataEncoding and the IndexRange are not specified if the AttributeId is not Value.
        /// </para>
        /// <para>
        /// The MasterNodeManager post-processes the values by:
        ///    - sets values[ii].StatusCode to the value of errors[ii].Code
        ///    - creates a instance of DataValue if one does not exist and an errors[ii] is bad.
        ///    - removes timestamps from the DataValue if the client does not want them.
        /// </para>
        /// <para>
        /// The node manager must ignore ReadValueId with the Processed flag set to true.
        /// The node manager must set the Processed flag for any ReadValueId that it processes.
        /// </para>
        /// </remarks>
        void Read(
            OperationContext context,
            double maxAge,
            IList<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors
        );

        /// <summary>
        /// Reads the history of a set of items.
        /// </summary>
        void HistoryRead(
            OperationContext context,
            HistoryReadDetails details,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors
        );

        /// <summary>
        /// Writes a set of values.
        /// </summary>
        /// <remarks>
        /// Each node manager should only process node ids that it recognizes. If it processes a value it
        /// must set the Processed flag in the WriteValue structure.
        /// </remarks>
        void Write(
            OperationContext context,
            IList<WriteValue> nodesToWrite,
            IList<ServiceResult> errors);

        /// <summary>
        /// Updates the history for a set of nodes.
        /// </summary>
        void HistoryUpdate(
            OperationContext context,
            Type detailsType,
            IList<HistoryUpdateDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors
        );

        /// <summary>
        /// Calls a method defined on an object.
        /// </summary>
        void Call(
            OperationContext context,
            IList<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors
        );

        /// <summary>
        /// Tells the NodeManager to report events from the specified notifier.
        /// </summary>
        /// <remarks>
        /// This method may be called multiple times for the name monitoredItemId if the
        /// context for that MonitoredItem changes (i.e. UserIdentity and/or Locales).
        /// </remarks>
        ServiceResult SubscribeToEvents(
            OperationContext context,
            object sourceId,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe
        );

        /// <summary>
        /// Tells the NodeManager to report events all events from all sources.
        /// </summary>
        /// <remarks>
        /// This method may be called multiple times for the name monitoredItemId if the
        /// context for that MonitoredItem changes (i.e. UserIdentity and/or Locales).
        /// </remarks>
        ServiceResult SubscribeToAllEvents(
            OperationContext context,
            uint subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool unsubscribe
        );

        /// <summary>
        /// Tells the NodeManager to refresh any conditions.
        /// </summary>
        ServiceResult ConditionRefresh(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems);

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
            IList<MonitoringFilterResult> filterErrors,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable,
            ref long globalIdCounter
        );

        /// <summary>
        /// Restore a set of monitored items after a restart.
        /// </summary>
        void RestoreMonitoredItems(
            IList<IStoredMonitoredItem> itemsToRestore,
            IList<IMonitoredItem> monitoredItems,
            IUserIdentity savedOwnerIdentity
        );

        /// <summary>
        /// Modifies a set of monitored items.
        /// </summary>
        void ModifyMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            IList<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterErrors
        );

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        void DeleteMonitoredItems(
            OperationContext context,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors
        );

        /// <summary>
        /// Transfers a set of monitored items.
        /// </summary>
        /// <remarks>
        /// Queue initial values from monitored items in the node managers.
        /// </remarks>
        void TransferMonitoredItems(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors
        );

        /// <summary>
        /// Changes the monitoring mode for a set of monitored items.
        /// </summary>
        void SetMonitoringMode(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors
        );
    }

    /// <summary>
    /// An interface to an object that manages a set of nodes in the address space.
    /// </summary>
    public interface INodeManager2 : INodeManager
    {
        /// <summary>
        /// Called when the session is closed.
        /// </summary>
        void SessionClosing(OperationContext context, NodeId sessionId, bool deleteSubscriptions);

        /// <summary>
        /// Returns true if the node is in the view.
        /// </summary>
        bool IsNodeInView(OperationContext context, NodeId viewId, object nodeHandle);

        /// <summary>
        /// Returns the metadata needed for validating permissions, associated with the node with
        /// the option to optimize services by using a cache.
        /// </summary>
        /// <remarks>
        /// Returns null if the node does not exist.
        /// It should return null in case the implementation wishes to handover the task to the parent INodeManager.GetNodeMetadata
        /// </remarks>
        NodeMetadata GetPermissionMetadata(
            OperationContext context,
            object targetHandle,
            BrowseResultMask resultMask,
            Dictionary<NodeId, List<object>> uniqueNodesServiceAttributesCache,
            bool permissionsOnly
        );
    }
    /// <summary>
    /// An asynchronous verson of the <see cref="INodeManager2"/> interface.
    /// </summary>
    public interface IAsyncNodeManager : INodeManager2
    {
        /// <summary>
        /// Asycnhronously calls a method defined on an object.
        /// </summary>
        ValueTask CallAsync(
            OperationContext context,
            IList<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> results,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Stores metadata required to process requests related to a node.
    /// </summary>
    public class NodeMetadata
    {
        /// <summary>
        /// Initializes the object with its handle and NodeId.
        /// </summary>
        public NodeMetadata(object handle, NodeId nodeId)
        {
            Handle = handle;
            NodeId = nodeId;
        }

        /// <summary>
        /// The handle assigned by the NodeManager that owns the Node.
        /// </summary>
        public object Handle { get; }

        /// <summary>
        /// The canonical NodeId for the Node.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// The NodeClass for the Node.
        /// </summary>
        public NodeClass NodeClass { get; set; }

        /// <summary>
        /// The BrowseName for the Node.
        /// </summary>
        public QualifiedName BrowseName { get; set; }

        /// <summary>
        /// The DisplayName for the Node.
        /// </summary>
        public LocalizedText DisplayName { get; set; }

        /// <summary>
        /// The type definition for the Node (if one exists).
        /// </summary>
        public ExpandedNodeId TypeDefinition { get; set; }

        /// <summary>
        /// The modelling for the Node (if one exists).
        /// </summary>
        public NodeId ModellingRule { get; set; }

        /// <summary>
        /// Specifies which attributes are writeable.
        /// </summary>
        public AttributeWriteMask WriteMask { get; set; }

        /// <summary>
        /// Whether the Node can be used with event subscriptions or for historial event queries.
        /// </summary>
        public byte EventNotifier { get; set; }

        /// <summary>
        /// Whether the Node can be use to read or write current or historical values.
        /// </summary>
        public byte AccessLevel { get; set; }

        /// <summary>
        /// Whether the Node is a Method that can be executed.
        /// </summary>
        public bool Executable { get; set; }

        /// <summary>
        /// The DataType of the Value attribute for Variable or VariableType nodes.
        /// </summary>
        public NodeId DataType { get; set; }

        /// <summary>
        /// The ValueRank for the Value attribute for Variable or VariableType nodes.
        /// </summary>
        public int ValueRank { get; set; }

        /// <summary>
        /// The ArrayDimensions for the Value attribute for Variable or VariableType nodes.
        /// </summary>
        public IList<uint> ArrayDimensions { get; set; }

        /// <summary>
        /// Specifies the AccessRestrictions that apply to a Node.
        /// </summary>
        public AccessRestrictionType AccessRestrictions { get; set; }

        /// <summary>
        /// The value reflects the DefaultAccessRestrictions Property of the NamespaceMetadata Object for the Namespace
        /// to which the Node belongs.
        /// </summary>
        public AccessRestrictionType DefaultAccessRestrictions { get; set; }

        /// <summary>
        /// The RolePermissions for the Node.
        /// Specifies the Permissions that apply to a Node for all Roles which have access to the Node.
        /// </summary>
        public RolePermissionTypeCollection RolePermissions { get; set; }

        /// <summary>
        /// The DefaultRolePermissions of the Node's name-space meta-data
        /// The value reflects the DefaultRolePermissions Property from the NamespaceMetadata Object associated with the Node.
        /// </summary>
        public RolePermissionTypeCollection DefaultRolePermissions { get; set; }

        /// <summary>
        /// The UserRolePermissions of the Node.
        /// Specifies the Permissions that apply to a Node for all Roles granted to current Session.
        /// </summary>
        public RolePermissionTypeCollection UserRolePermissions { get; set; }

        /// <summary>
        /// The DefaultUserRolePermissions of the Node.
        /// The value reflects the DefaultUserRolePermissions Property from the NamespaceMetadata Object associated with the Node.
        /// </summary>
        public RolePermissionTypeCollection DefaultUserRolePermissions { get; set; }
    }
}
