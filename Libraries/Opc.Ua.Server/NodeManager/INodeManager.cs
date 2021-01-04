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
using System.Text;

namespace Opc.Ua.Server
{
    /// <summary>
    /// An interface to an object that manages a set of nodes in the address space.
    /// </summary>
    public interface INodeManager
    {        
        /// <summary>
        /// Returns the NamespaceUris for the Nodes belonging to the NodeManager.
        /// </summary>
        /// <remarks>
        /// By default the MasterNodeManager uses the namespaceIndex to determine who owns an Node.
        /// 
        /// Servers that do not wish to partition their address space this way must provide their own
        /// implementation of MasterNodeManager.GetManagerHandle().
        /// 
        /// NodeManagers which depend on a custom partitioning scheme must return a null value.
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
        void CreateAddressSpace(IDictionary<NodeId,IList<IReference>> externalReferences);
        
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
        void AddReferences(IDictionary<NodeId,IList<IReference>> references);
               
        /// <summary>
        /// Deletes a reference.
        /// </summary>
        ServiceResult DeleteReference(
            object         sourceHandle, 
            NodeId         referenceTypeId,
            bool           isInverse, 
            ExpandedNodeId targetId, 
            bool           deleteBidirectional);
         
        /// <summary>
        /// Returns the metadata associated with the node.
        /// </summary>
        /// <remarks>
        /// Returns null if the node does not exist.
        /// </remarks>
        NodeMetadata GetNodeMetadata(
            OperationContext context,
            object           targetHandle,
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
        /// include these references when calculating whether a continutation point must be returned.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if the context, continuationPoint or references parameters are null.</exception>
        /// <exception cref="ServiceResultException">Thrown if an error occurs during processing.</exception>
        void Browse(
            OperationContext            context,
            ref ContinuationPoint       continuationPoint,
            IList<ReferenceDescription> references);

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
            OperationContext      context,
            object                sourceHandle, 
            RelativePathElement   relativePath, 
            IList<ExpandedNodeId> targetIds,
            IList<NodeId>         unresolvedTargetIds);

        /// <summary>
        /// Reads the attribute values for a set of nodes.
        /// </summary>
        /// <remarks>
        /// The MasterNodeManager pre-processes the nodesToRead and ensures that:
        ///    - the AttributeId is a known attribute.
        ///    - the IndexRange, if specified, is valid.
        ///    - the DataEncoding and the IndexRange are not specified if the AttributeId is not Value.
        ///
        /// The MasterNodeManager post-processes the values by:
        ///    - sets values[ii].StatusCode to the value of errors[ii].Code
        ///    - creates a instance of DataValue if one does not exist and an errors[ii] is bad.
        ///    - removes timestamps from the DataValue if the client does not want them.
        /// 
        /// The node manager must ignore ReadValueId with the Processed flag set to true.
        /// The node manager must set the Processed flag for any ReadValueId that it processes.
        /// </remarks>
        void Read(
            OperationContext     context,
            double               maxAge,
            IList<ReadValueId>   nodesToRead,
            IList<DataValue>     values,
            IList<ServiceResult> errors);
        
        /// <summary>
        /// Reads the history of a set of items.
        /// </summary>
        void HistoryRead(
            OperationContext          context,
            HistoryReadDetails        details, 
            TimestampsToReturn        timestampsToReturn, 
            bool                      releaseContinuationPoints, 
            IList<HistoryReadValueId> nodesToRead, 
            IList<HistoryReadResult>  results, 
            IList<ServiceResult>      errors);

        /// <summary>
        /// Writes a set of values.
        /// </summary>
        /// <remarks>
        /// Each node manager should only process node ids that it recognizes. If it processes a value it
        /// must set the Processed flag in the WriteValue structure.
        /// </remarks>
        void Write(
            OperationContext     context,
            IList<WriteValue>    nodesToWrite, 
            IList<ServiceResult> errors);
        
        /// <summary>
        /// Updates the history for a set of nodes.
        /// </summary>
        void HistoryUpdate(
            OperationContext            context,
            Type                        detailsType,
            IList<HistoryUpdateDetails> nodesToUpdate, 
            IList<HistoryUpdateResult>  results, 
            IList<ServiceResult>        errors);

        /// <summary>
        /// Calls a method defined on a object.
        /// </summary>
        void Call(
            OperationContext         context,
            IList<CallMethodRequest> methodsToCall,
            IList<CallMethodResult>  results,
            IList<ServiceResult>     errors);

        /// <summary>
        /// Tells the NodeManager to report events from the specified notifier.
        /// </summary>
        /// <remarks>
        /// This method may be called multiple times for the name monitoredItemId if the
        /// context for that MonitoredItem changes (i.e. UserIdentity and/or Locales).
        /// </remarks>
        ServiceResult SubscribeToEvents(            
            OperationContext    context,
            object              sourceId,
            uint                subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool                unsubscribe);
        
        /// <summary>
        /// Tells the NodeManager to report events all events from all sources.
        /// </summary>
        /// <remarks>
        /// This method may be called multiple times for the name monitoredItemId if the
        /// context for that MonitoredItem changes (i.e. UserIdentity and/or Locales).
        /// </remarks>
        ServiceResult SubscribeToAllEvents(            
            OperationContext   context,
            uint                subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool                unsubscribe);
        
        /// <summary>
        /// Tells the NodeManager to refresh any conditions.
        /// </summary>
        ServiceResult ConditionRefresh(            
            OperationContext           context,
            IList<IEventMonitoredItem> monitoredItems);

        /// <summary>
        /// Creates a set of monitored items.
        /// </summary>
        void CreateMonitoredItems(
            OperationContext                  context,
            uint                              subscriptionId,
            double                            publishingInterval,
            TimestampsToReturn                timestampsToReturn,
            IList<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult>              errors,
            IList<MonitoringFilterResult>     filterErrors,
            IList<IMonitoredItem>             monitoredItems,
            ref long                          globalIdCounter);
                
        /// <summary>
        /// Modifies a set of monitored items.
        /// </summary>
        void ModifyMonitoredItems(
            OperationContext                  context,
            TimestampsToReturn                timestampsToReturn,
            IList<IMonitoredItem>             monitoredItems,
            IList<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult>              errors,
            IList<MonitoringFilterResult>     filterErrors);

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        void DeleteMonitoredItems(
            OperationContext      context,
            IList<IMonitoredItem> monitoredItems, 
            IList<bool>           processedItems,
            IList<ServiceResult>  errors);
        
        /// <summary>
        /// Changes the monitoring mode for a set of monitored items.
        /// </summary>
        void SetMonitoringMode(
            OperationContext      context,
            MonitoringMode        monitoringMode,
            IList<IMonitoredItem> monitoredItems, 
            IList<bool>           processedItems,
            IList<ServiceResult>  errors);
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
    }
    
    /// <summary>
    /// Stores metadata required to process requests related to a node.
    /// </summary>
    public class NodeMetadata
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with its handle and NodeId.
        /// </summary>
        public NodeMetadata(object handle, NodeId nodeId)
        {
            m_handle = handle;
            m_nodeId = nodeId;
        }
        #endregion
        
        #region Public Properties
        /// <summary>
        /// The handle assigned by the NodeManager that owns the Node.
        /// </summary>
        public object Handle
        {
            get { return m_handle; }
        }

        /// <summary>
        /// The canonical NodeId for the Node.
        /// </summary>
        public NodeId NodeId
        {
            get { return m_nodeId; }
        }        

        /// <summary>
        /// The NodeClass for the Node.
        /// </summary>
        public NodeClass NodeClass
        {
            get { return m_nodeClass;  }
            set { m_nodeClass = value; }
        }

        /// <summary>
        /// The BrowseName for the Node.
        /// </summary>
        public QualifiedName BrowseName
        {
            get { return m_browseName;  }
            set { m_browseName = value; }
        }

        /// <summary>
        /// The DisplayName for the Node.
        /// </summary>
        public LocalizedText DisplayName
        {
            get { return m_displayName;  }
            set { m_displayName = value; }
        }

        /// <summary>
        /// The type definition for the Node (if one exists).
        /// </summary>
        public ExpandedNodeId TypeDefinition
        {
            get { return m_typeDefinition;  }
            set { m_typeDefinition = value; }
        }

        /// <summary>
        /// The modelling for the Node (if one exists).
        /// </summary>
        public NodeId ModellingRule
        {
            get { return m_modellingRule;  }
            set { m_modellingRule = value; }
        }

        /// <summary>
        /// Specifies which attributes are writeable.
        /// </summary>
        public AttributeWriteMask WriteMask
        {
            get { return m_writeMask;  }
            set { m_writeMask = value; }
        }

        /// <summary>
        /// Whether the Node can be used with event subscriptions or for historial event queries.
        /// </summary>
        public byte EventNotifier
        {
            get { return m_eventNotifier;  }
            set { m_eventNotifier = value; }
        }
        
        /// <summary>
        /// Whether the Node can be use to read or write current or historical values.
        /// </summary>
        public byte AccessLevel
        {
            get { return m_accessLevel;  }
            set { m_accessLevel = value; }
        }

        /// <summary>
        /// Whether the Node is a Method that can be executed.
        /// </summary>
        public bool Executable
        {
            get { return m_executable;  }
            set { m_executable = value; }
        }

        /// <summary>
        /// The DataType of the Value attribute for Variable or VariableType nodes.
        /// </summary>
        public NodeId DataType
        {
            get { return m_dataType;  }
            set { m_dataType = value; }
        }

        /// <summary>
        /// The ValueRank for the Value attribute for Variable or VariableType nodes.
        /// </summary>
        public int ValueRank
        {
            get { return m_valueRank;  }
            set { m_valueRank = value; }
        }

        /// <summary>
        /// The ArrayDimensions for the Value attribute for Variable or VariableType nodes.
        /// </summary>
        public IList<uint> ArrayDimensions
        {
            get { return m_arrayDimensions;  }
            set { m_arrayDimensions = value; }
        }

        /// <summary>
        /// Specifies the AccessRestrictions that apply to a Node.
        /// </summary>
        public AccessRestrictionType AccessRestrictions
        {
            get { return m_accessRestrictions; }
            set { m_accessRestrictions = value; }
        }

        /// <summary>
        /// The value reflects the DefaultAccessRestrictions Property of the NamespaceMetadata Object for the Namespace
        /// to which the Node belongs.
        /// </summary>
        public AccessRestrictionType DefaultAccessRestrictions
        {
            get { return m_defaultAccessRestrictions; }
            set { m_defaultAccessRestrictions = value; }
        }

        /// <summary>
        /// The RolePermissions for the Node.
        /// Specifies the Permissions that apply to a Node for all Roles which have access to the Node.
        /// </summary>
        public RolePermissionTypeCollection RolePermissions
        {
            get { return m_rolePermissions; }
            set { m_rolePermissions = value; }
        }

        /// <summary>
        /// The DefaultRolePermissions of the Node's name-space meta-data
        /// The value reflects the DefaultRolePermissions Property from the NamespaceMetadata Object associated with the Node.
        /// </summary>
        public RolePermissionTypeCollection DefaultRolePermissions
        {
            get { return m_defaultRolePermissions; }
            set { m_defaultRolePermissions = value; }
        }

        /// <summary>
        /// The UserRolePermissions of the Node.
        /// Specifies the Permissions that apply to a Node for all Roles granted to current Session.
        /// </summary>
        public RolePermissionTypeCollection UserRolePermissions
        {
            get { return m_userRolePermissions; }
            set { m_userRolePermissions = value; }
        }

        /// <summary>
        /// The DefaultUserRolePermissions of the Node.
        /// The value reflects the DefaultUserRolePermissions Property from the NamespaceMetadata Object associated with the Node.
        /// </summary>
        public RolePermissionTypeCollection DefaultUserRolePermissions
        {
            get { return m_defaultUserRolePermissions; }
            set { m_defaultUserRolePermissions = value; }
        }
        #endregion

        #region Private Fields
        private object m_handle;
        private NodeId m_nodeId;
        private NodeClass m_nodeClass;
        private QualifiedName m_browseName;
        private LocalizedText m_displayName;
        private ExpandedNodeId m_typeDefinition;
        private NodeId m_modellingRule;
        private AttributeWriteMask m_writeMask;
        private byte m_eventNotifier;
        private byte m_accessLevel;
        private bool m_executable;
        private NodeId m_dataType;
        private int m_valueRank;
        private IList<uint> m_arrayDimensions;
        private AccessRestrictionType m_accessRestrictions;
        private AccessRestrictionType m_defaultAccessRestrictions;
        private RolePermissionTypeCollection m_rolePermissions;
        private RolePermissionTypeCollection m_defaultRolePermissions;
        private RolePermissionTypeCollection m_userRolePermissions;
        private RolePermissionTypeCollection m_defaultUserRolePermissions;
        #endregion
    }
}
