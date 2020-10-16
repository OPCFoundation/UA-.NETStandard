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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Threading;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

#pragma warning disable 0618

namespace Opc.Ua.Server
{
    /// <summary>
    /// The default node manager for the server.
    /// </summary>
    /// <remarks>
    /// Every Server has one instance of this NodeManager. 
    /// It stores objects that implement ILocalNode and indexes them by NodeId.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
    public partial class CoreNodeManager : INodeManager, IDisposable
    {        
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public CoreNodeManager(
            IServerInternal          server,
            ApplicationConfiguration configuration,
            ushort                   dynamicNamespaceIndex)
        {
            if (server == null)        throw new ArgumentNullException(nameof(server));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
                  
            m_server                         = server;
            m_nodes                          = new NodeTable(server.NamespaceUris, server.ServerUris, server.TypeTree);
            m_monitoredItems                 = new Dictionary<uint,MonitoredItem>();
            m_defaultMinimumSamplingInterval = 1000;
            m_namespaceUris                  = new List<string>();
            m_dynamicNamespaceIndex          = dynamicNamespaceIndex; 
            
            // use namespace 1 if out of range.
            if (m_dynamicNamespaceIndex == 0 || m_dynamicNamespaceIndex >= server.NamespaceUris.Count)
            {
                m_dynamicNamespaceIndex = 1;
            }

            m_samplingGroupManager = new SamplingGroupManager(
                server, 
                this,
                (uint)configuration.ServerConfiguration.MaxNotificationQueueSize,
                configuration.ServerConfiguration.AvailableSamplingRates);
        }
        #endregion
        
        #region IDisposable Members        
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {  
            if (disposing)
            {
                List<INode> nodes = null;                

                lock(m_lock)
                {
                    nodes = new List<INode>(m_nodes);
                    m_nodes.Clear();

                    m_monitoredItems.Clear();
                }

                foreach (INode node in nodes)
                {
                    Utils.SilentDispose(node);
                }

                Utils.SilentDispose(m_samplingGroupManager);
            }
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Acquires the lock on the node manager.
        /// </summary>
        public object DataLock
        {
            get { return m_lock; }
        }
        #endregion

        /// <summary>
        /// Imports the nodes from a dictionary of NodeState objects.
        /// </summary>
        public void ImportNodes(ISystemContext context, IEnumerable<NodeState> predefinedNodes)
        {
            ImportNodes(context, predefinedNodes, false);
        }

        /// <summary>
        /// Imports the nodes from a dictionary of NodeState objects.
        /// </summary>
        internal void ImportNodes(ISystemContext context, IEnumerable<NodeState> predefinedNodes, bool isInternal)
        {
            NodeTable nodesToExport = new NodeTable(Server.NamespaceUris, Server.ServerUris, Server.TypeTree);

            foreach (NodeState node in predefinedNodes)
            {
                node.Export(context, nodesToExport);
            }

            lock (Server.CoreNodeManager.DataLock)
            {
                foreach (ILocalNode nodeToExport in nodesToExport)
                {
                    Server.CoreNodeManager.AttachNode(nodeToExport, isInternal);
                }
            }
        }
 
        #region INodeManager Members
        /// <summary cref="INodeManager.NamespaceUris" />
        public IEnumerable<string> NamespaceUris
        {
            get
            {
                return m_namespaceUris;
            }
        }

        /// <summary cref="INodeManager.CreateAddressSpace" />
        /// <remarks>
        /// Populates the NodeManager by loading the standard nodes from an XML file stored as an embedded resource.
        /// </remarks>
        public void CreateAddressSpace(IDictionary<NodeId,IList<IReference>> externalReferences)
        {
            // TBD
        }
                
        /// <summary cref="INodeManager.DeleteAddressSpace" />
        /// <remarks>
        /// Disposes all of the nodes.
        /// </remarks>
        public void DeleteAddressSpace()
        {
            List<IDisposable> nodesToDispose = new List<IDisposable>();

            lock(m_lock)
            {
                // collect nodes to dispose.
                foreach (INode node in m_nodes)
                {
                    IDisposable disposable = node as IDisposable;

                    if (disposable != null)
                    {
                        nodesToDispose.Add(disposable);
                    }
                }

                m_nodes.Clear();    
            }

            // dispose of the nodes.
            foreach (IDisposable disposable in nodesToDispose)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error disposing a Node object.");
                }
            }
        }
        
        /// <see cref="INodeManager.GetManagerHandle" />
        public object GetManagerHandle(NodeId nodeId)
        {
            lock(m_lock)
            {
                if (NodeId.IsNull(nodeId))
                {
                    return null;
                }

                return GetLocalNode(nodeId);
            }
        }
                
        /// <see cref="INodeManager.TranslateBrowsePath(OperationContext,object,RelativePathElement,IList{ExpandedNodeId},IList{NodeId})" />
        public void TranslateBrowsePath(
            OperationContext      context,
            object                sourceHandle, 
            RelativePathElement   relativePath, 
            IList<ExpandedNodeId> targetIds,
            IList<NodeId>         unresolvedTargetIds)
        {
            if (sourceHandle == null) throw new ArgumentNullException(nameof(sourceHandle));
            if (relativePath == null) throw new ArgumentNullException(nameof(relativePath));
            if (targetIds == null) throw new ArgumentNullException(nameof(targetIds));
            if (unresolvedTargetIds == null) throw new ArgumentNullException(nameof(unresolvedTargetIds));

            // check for valid handle.
            ILocalNode source = sourceHandle as ILocalNode;

            if (source == null)
            {
                return;
            }
            
            lock(m_lock)
            {
                // find the references that meet the filter criteria.
                IList<IReference> references = source.References.Find(
                    relativePath.ReferenceTypeId, 
                    relativePath.IsInverse, 
                    relativePath.IncludeSubtypes, 
                    m_server.TypeTree);

                // nothing more to do.
                if (references == null || references.Count == 0)
                {
                    return;
                }

                // find targets with matching browse names.
                foreach (IReference reference in references)
                {
                    INode target = GetLocalNode(reference.TargetId);

                    // target is not known to the node manager.
                    if (target == null)
                    {
                        // ignore unknown external references.
                        if (reference.TargetId.IsAbsolute)
                        {
                            continue;
                        }

                        // caller must check the browse name.
                        unresolvedTargetIds.Add((NodeId)reference.TargetId);
                        continue;
                    }

                    // check browse name.
                    if (target.BrowseName == relativePath.TargetName)
                    {
                        targetIds.Add(reference.TargetId);
                    }
                }
            }
        }

        #region Browse
        /// <see cref="INodeManager.Browse" />
        public void Browse(
            OperationContext            context,
            ref ContinuationPoint       continuationPoint,
            IList<ReferenceDescription> references)
        {              
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (continuationPoint == null) throw new ArgumentNullException(nameof(continuationPoint));
            if (references == null) throw new ArgumentNullException(nameof(references));
            
            // check for valid handle.
            ILocalNode source = continuationPoint.NodeToBrowse as ILocalNode;

            if (source == null)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            
            // check for view.
            if (!ViewDescription.IsDefault(continuationPoint.View))
            {
                throw new ServiceResultException(StatusCodes.BadViewIdUnknown);
            }

            lock (m_lock)
            {
                // construct list of references.
                uint maxResultsToReturn = continuationPoint.MaxResultsToReturn;

                // get previous enumerator.
                IEnumerator<IReference> enumerator = continuationPoint.Data as IEnumerator<IReference>;
            
                // fetch a snapshot all references for node.
                if (enumerator == null)
                {
                    List<IReference> copy = new List<IReference>(source.References);
                    enumerator = copy.GetEnumerator();
                    enumerator.MoveNext();
                }

                do
                {
                    IReference reference = enumerator.Current;

                    // silently ignore bad values.
                    if (reference == null || NodeId.IsNull(reference.ReferenceTypeId) || NodeId.IsNull(reference.TargetId))
                    {
                        continue;
                    }

                    // apply browse filters.
                    bool include = ApplyBrowseFilters(
                        reference,
                        continuationPoint.BrowseDirection,
                        continuationPoint.ReferenceTypeId,
                        continuationPoint.IncludeSubtypes);

                    if (include)
                    {                 
                        ReferenceDescription description = new ReferenceDescription();
                        
                        description.NodeId = reference.TargetId;
                        description.SetReferenceType(continuationPoint.ResultMask, reference.ReferenceTypeId, !reference.IsInverse);

                        // only fetch the metadata if it is requested.
                        if (continuationPoint.TargetAttributesRequired)
                        {                        
                            // get the metadata for the node.
                            NodeMetadata metadata = GetNodeMetadata(context, GetManagerHandle(reference.TargetId), continuationPoint.ResultMask);

                            // update description with local node metadata.
                            if (metadata != null)
                            {
                                description.SetTargetAttributes(
                                    continuationPoint.ResultMask,
                                    metadata.NodeClass,
                                    metadata.BrowseName,
                                    metadata.DisplayName,
                                    metadata.TypeDefinition);

                                // check node class mask.
                                if (!CheckNodeClassMask(continuationPoint.NodeClassMask, description.NodeClass))
                                {
                                    continue;
                                }
                            }

                            // any target that is not remote must be owned by another node manager.
                            else if (!reference.TargetId.IsAbsolute)
                            {
                                description.Unfiltered = true;
                            }
                        }

                        // add reference to list.
                        references.Add(description);

                        // construct continuation point if max results reached.
                        if (maxResultsToReturn > 0 && references.Count >= maxResultsToReturn)
                        { 
                            continuationPoint.Index = 0;
                            continuationPoint.Data  = enumerator;
                            enumerator.MoveNext();
                            return;
                        }
                    }
                }
                while (enumerator.MoveNext());
                
                // nothing more to browse if it exits from the loop normally.
                continuationPoint.Dispose();
                continuationPoint = null;
            }
        }
        
        /// <summary>
        /// Returns true is the target meets the filter criteria.
        /// </summary>
        private bool ApplyBrowseFilters(
            IReference      reference,
            BrowseDirection browseDirection,
            NodeId          referenceTypeId,
            bool            includeSubtypes)
        {
            // check browse direction.
            if (reference.IsInverse)
            {
                if (browseDirection == BrowseDirection.Forward)
                {
                    return false;
                }
            }
            else
            {
                if (browseDirection == BrowseDirection.Inverse)
                {
                    return false;
                }
            }

            // check reference type filter.
            if (!NodeId.IsNull(referenceTypeId))
            {
                if (reference.ReferenceTypeId != referenceTypeId)
                {
                    if (includeSubtypes)
                    {
                        if (m_server.TypeTree.IsTypeOf(reference.ReferenceTypeId, referenceTypeId))
                        {
                            return true;
                        }
                    }
                        
                    return false;
                }
            }
                   
            // include reference for now.
            return true;
        }
        #endregion
        
        /// <see cref="INodeManager.GetNodeMetadata" />
        public NodeMetadata GetNodeMetadata(
            OperationContext context,
            object           targetHandle,
            BrowseResultMask resultMask)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            
            // find target.
            ILocalNode target = targetHandle as ILocalNode;

            if (target == null)
            {
                return null;
            }

            lock (m_lock)
            {
                // copy the default metadata.
                NodeMetadata metadata = new NodeMetadata(target, target.NodeId);
                
                // copy target attributes.
                if ((resultMask & BrowseResultMask.NodeClass) != 0)
                {
                    metadata.NodeClass = (NodeClass)target.NodeClass;
                }

                if ((resultMask & BrowseResultMask.BrowseName) != 0)
                {
                    metadata.BrowseName = target.BrowseName;
                }
                
                if ((resultMask & BrowseResultMask.DisplayName) != 0)
                {
                    metadata.DisplayName = target.DisplayName;

                    // check if the display name can be localized.
                    if (!String.IsNullOrEmpty(metadata.DisplayName.Key))
                    {
                        metadata.DisplayName = Server.ResourceManager.Translate(context.PreferredLocales, metadata.DisplayName);
                    }
                }
                
                metadata.WriteMask = target.WriteMask;

                if (metadata.WriteMask != AttributeWriteMask.None)
                {
                    DataValue value = new DataValue((uint)(int)target.UserWriteMask);
                    ServiceResult result = target.Read(context, Attributes.UserWriteMask, value);

                    if (ServiceResult.IsBad(result))
                    {
                        metadata.WriteMask = AttributeWriteMask.None;
                    }
                    else
                    {
                        metadata.WriteMask = (AttributeWriteMask)(int)((uint)(int)metadata.WriteMask & (uint)value.Value);
                    }
                }

                metadata.EventNotifier = EventNotifiers.None;
                metadata.AccessLevel   = AccessLevels.None;
                metadata.Executable    = false;
                
                switch (target.NodeClass)
                {
                    case NodeClass.Object:
                    {
                        metadata.EventNotifier = ((IObject)target).EventNotifier;
                        break;
                    }

                    case NodeClass.View:
                    {
                        metadata.EventNotifier = ((IView)target).EventNotifier;
                        break;
                    }

                    case NodeClass.Variable:
                    {
                        IVariable variable = (IVariable)target;
                        metadata.DataType = variable.DataType;
                        metadata.ValueRank = variable.ValueRank;
                        metadata.ArrayDimensions = variable.ArrayDimensions;                        
                        metadata.AccessLevel = variable.AccessLevel;

                        DataValue value = new DataValue(variable.UserAccessLevel);
                        ServiceResult result = variable.Read(context, Attributes.UserAccessLevel, value);

                        if (ServiceResult.IsBad(result))
                        {
                            metadata.AccessLevel = 0;
                            break;
                        }
                        
                        metadata.AccessLevel = (byte)(metadata.AccessLevel & (byte)value.Value);
                        break;
                    }

                    case NodeClass.Method:
                    {
                        IMethod method = (IMethod)target;
                        metadata.Executable = method.Executable;

                        if (metadata.Executable)
                        {
                            DataValue value = new DataValue(method.UserExecutable);
                            ServiceResult result = method.Read(context, Attributes.UserExecutable, value);

                            if (ServiceResult.IsBad(result))
                            {
                                metadata.Executable = false;
                                break;
                            }
                            
                            metadata.Executable = (bool)value.Value;
                        }

                        break;
                    }
                }
                
                // look up type definition.
                if ((resultMask & BrowseResultMask.TypeDefinition) != 0)
                {
                    if (target.NodeClass == NodeClass.Variable || target.NodeClass == NodeClass.Object)
                    {
                        metadata.TypeDefinition = target.TypeDefinitionId;
                    }
                }

                // Set AccessRestrictions and RolePermissions
                Node node = (Node)target;
                metadata.AccessRestrictions = (AccessRestrictionType)Enum.Parse(typeof(AccessRestrictionType), node.AccessRestrictions.ToString()); 
                metadata.RolePermissions = node.RolePermissions;
                metadata.UserRolePermissions = node.UserRolePermissions;

                // check if NamespaceMetadata is defined for NamespaceUri
                string namespaceUri = Server.NamespaceUris.GetString(target.NodeId.NamespaceIndex);
                NamespaceMetadataState namespaceMetadataState = Server.NodeManager.ConfigurationNodeManager.GetNamespaceMetadataState(namespaceUri);
                if (namespaceMetadataState != null)
                {
                    metadata.DefaultAccessRestrictions = (AccessRestrictionType)Enum.ToObject(typeof(AccessRestrictionType),
                        namespaceMetadataState.DefaultAccessRestrictions.Value);
                  
                    metadata.DefaultRolePermissions = namespaceMetadataState.DefaultRolePermissions.Value;
                    metadata.DefaultUserRolePermissions = namespaceMetadataState.DefaultUserRolePermissions.Value;
                }

                #if LEGACY_NODEMANAGER
                // check if a source is defined for the node.
                SourceHandle handle = target.Handle as SourceHandle;

                if (handle != null)
                {
                    // check if the metadata needs to be updated by the source.
                    IReadMetadataSource source = handle.Source as IReadMetadataSource;

                    if (source != null)
                    {
                        source.ReadMetadata(
                            context,
                            handle.Handle,
                            resultMask,
                            metadata);
                    }
                }
                #endif

                // return metadata.
                return metadata;
            }
        }

        /// <summary cref="INodeManager.AddReferences" />
        /// <remarks>
        /// This method must not be called without first acquiring 
        /// </remarks>
        public void AddReferences(IDictionary<NodeId,IList<IReference>> references)
        {
            if (references == null) throw new ArgumentNullException(nameof(references));

            lock (m_lock)
            {
                IEnumerator<KeyValuePair<NodeId,IList<IReference>>> enumerator = references.GetEnumerator();

                while (enumerator.MoveNext())
                {
                    ILocalNode actualNode = GetLocalNode(enumerator.Current.Key) as ILocalNode;

                    if (actualNode != null)
                    {
                        foreach (IReference reference in enumerator.Current.Value)
                        {
                            AddReference(actualNode, reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                        }
                    }                        
                }
            }
        }

        /// <see cref="INodeManager.Read" />
        public void Read(
            OperationContext     context,
            double               maxAge,
            IList<ReadValueId>   nodesToRead,
            IList<DataValue>     values,
            IList<ServiceResult> errors)
        {
            if (context == null)     throw new ArgumentNullException(nameof(context));
            if (nodesToRead == null) throw new ArgumentNullException(nameof(nodesToRead));
            if (values == null)      throw new ArgumentNullException(nameof(values));
            if (errors == null)      throw new ArgumentNullException(nameof(errors));

            lock (m_lock)
            {
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    ReadValueId nodeToRead = nodesToRead[ii];

                    // skip items that have already been processed.
                    if (nodeToRead.Processed)
                    {
                        continue;
                    }
                    
                    // look up the node.
                    ILocalNode node = GetLocalNode(nodeToRead.NodeId) as ILocalNode;

                    if (node == null)
                    {
                        continue;
                    }

                    DataValue value = values[ii] = new DataValue();
                    
                    value.Value           = null;
                    value.ServerTimestamp = DateTime.UtcNow;
                    value.SourceTimestamp = DateTime.MinValue;
                    value.StatusCode      = StatusCodes.BadAttributeIdInvalid;

                    // owned by this node manager.
                    nodeToRead.Processed = true;
                    
                    // read the default value (also verifies that the attribute id is valid for the node).                   
                    ServiceResult error = node.Read(context, nodeToRead.AttributeId, value);

                    if (ServiceResult.IsBad(error))
                    {
                        errors[ii] = error;
                        continue;
                    }                    

                    // always use default value for base attributes.
                    bool useDefault = false;

                    switch (nodeToRead.AttributeId)
                    {
                        case Attributes.NodeId:
                        case Attributes.NodeClass:
                        case Attributes.BrowseName:
                        {
                            useDefault = true;
                            break;
                        }
                    }

                    if (useDefault)
                    {
                        errors[ii] = error;
                        continue;
                    }
                      
                    // apply index range to value attributes.
                    if (nodeToRead.AttributeId == Attributes.Value)
                    {
                        object defaultValue = value.Value;

                        error = nodeToRead.ParsedIndexRange.ApplyRange(ref defaultValue);
                    
                        if (ServiceResult.IsBad(error))
                        {
                            value.Value = null;
                            errors[ii] = error;
                            continue;
                        }
                        
                        // apply data encoding.
                        if (!QualifiedName.IsNull(nodeToRead.DataEncoding))
                        {
                            error = EncodeableObject.ApplyDataEncoding(Server.MessageContext, nodeToRead.DataEncoding, ref defaultValue);
                                
                            if (ServiceResult.IsBad(error))
                            {
                                value.Value = null;
                                errors[ii] = error;
                                continue;
                            }
                        }
                            
                        value.Value = defaultValue;                     
                        
                        // don't replace timestamp if it was set in the NodeSource 
                        if (value.SourceTimestamp == DateTime.MinValue) 
                        { 
                            value.SourceTimestamp = DateTime.UtcNow; 
                        } 
                    }
                }
            }   
                
        }
 
        /// <see cref="INodeManager.HistoryRead" />
        public void HistoryRead(
            OperationContext          context,
            HistoryReadDetails        details, 
            TimestampsToReturn        timestampsToReturn, 
            bool                      releaseContinuationPoints, 
            IList<HistoryReadValueId> nodesToRead, 
            IList<HistoryReadResult>  results, 
            IList<ServiceResult>      errors) 
        {
            if (context == null)     throw new ArgumentNullException(nameof(context));
            if (details == null)     throw new ArgumentNullException(nameof(details));
            if (nodesToRead == null) throw new ArgumentNullException(nameof(nodesToRead));
            if (results == null)     throw new ArgumentNullException(nameof(results));
            if (errors == null)      throw new ArgumentNullException(nameof(errors));

            ReadRawModifiedDetails readRawModifiedDetails = details as ReadRawModifiedDetails;
            ReadAtTimeDetails readAtTimeDetails = details as ReadAtTimeDetails;
            ReadProcessedDetails readProcessedDetails = details as ReadProcessedDetails;
            ReadEventDetails readEventDetails = details as ReadEventDetails;

            lock (m_lock)
            {
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    HistoryReadValueId nodeToRead = nodesToRead[ii];

                    // skip items that have already been processed.
                    if (nodeToRead.Processed)
                    {
                        continue;
                    }
                    
                    // look up the node.
                    ILocalNode node = GetLocalNode(nodeToRead.NodeId) as ILocalNode;

                    if (node == null)
                    {
                        continue;
                    }
                    
                    // owned by this node manager.
                    nodeToRead.Processed = true;

                    errors[ii] = StatusCodes.BadNotReadable;
                }
                
            }
            
        }

        /// <see cref="INodeManager.Write" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public void Write(
            OperationContext     context,
            IList<WriteValue>    nodesToWrite, 
            IList<ServiceResult> errors)
        {
            if (context == null)      throw new ArgumentNullException(nameof(context));
            if (nodesToWrite == null) throw new ArgumentNullException(nameof(nodesToWrite));
            if (errors == null)       throw new ArgumentNullException(nameof(errors));

            lock (m_lock)
            {
                for (int ii = 0; ii < nodesToWrite.Count; ii++)
                {
                    WriteValue nodeToWrite = nodesToWrite[ii];

                    // skip items that have already been processed.
                    if (nodeToWrite.Processed)
                    {
                        continue;
                    }
                    
                    // look up the node.
                    ILocalNode node = GetLocalNode(nodeToWrite.NodeId) as ILocalNode;

                    if (node == null)
                    {
                        continue;
                    }
                    
                    // owned by this node manager.
                    nodeToWrite.Processed = true;

                    if (!node.SupportsAttribute(nodeToWrite.AttributeId))
                    {
                        errors[ii] = StatusCodes.BadAttributeIdInvalid;
                        continue;
                    }
                    
                    // fetch the node metadata.
                    NodeMetadata metadata = GetNodeMetadata(context, node, BrowseResultMask.All);
                    
                    // check access.
                    bool writeable = true;
                    ServiceResult error = null;

                    // determine access rights.
                    switch (nodeToWrite.AttributeId)
                    {
                        case Attributes.NodeId:
                        case Attributes.NodeClass:
                        case Attributes.AccessLevel:
                        case Attributes.UserAccessLevel:
                        case Attributes.Executable:
                        case Attributes.UserExecutable:
                        case Attributes.EventNotifier:
                        {
                            writeable = false;
                            break;
                        }

                        case Attributes.Value:
                        {
                            writeable = ((metadata.AccessLevel & AccessLevels.CurrentWrite)!= 0);
                            break;
                        }

                        default:
                        {
                            writeable = (metadata.WriteMask & Attributes.GetMask(nodeToWrite.AttributeId)) != 0;
                            break;
                        }
                    }

                    // error if not writeable.
                    if (!writeable)
                    {
                        errors[ii] = StatusCodes.BadNotWritable;
                        continue;
                    }

                    // determine expected datatype and value rank.
                    NodeId expectedDatatypeId = metadata.DataType;
                    int expectedValueRank = metadata.ValueRank;
                    
                    if (nodeToWrite.AttributeId != Attributes.Value)
                    {
                        expectedDatatypeId = Attributes.GetDataTypeId(nodeToWrite.AttributeId);

                        DataValue value = nodeToWrite.Value;

                        if (value.StatusCode != StatusCodes.Good || value.ServerTimestamp != DateTime.MinValue || value.SourceTimestamp != DateTime.MinValue)
                        {
                            errors[ii] = StatusCodes.BadWriteNotSupported;
                            continue;
                        }

                        expectedValueRank = ValueRanks.Scalar;

                        if (nodeToWrite.AttributeId == Attributes.ArrayDimensions)
                        {
                            expectedValueRank = ValueRanks.OneDimension;
                        }
                    }

                    // check whether value being written is an instance of the expected data type.
                    object valueToWrite = nodeToWrite.Value.Value;

                    TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                        valueToWrite,
                        expectedDatatypeId,
                        expectedValueRank,
                        m_server.NamespaceUris,
                        m_server.TypeTree);

                    if (typeInfo == null)
                    {
                        errors[ii] = StatusCodes.BadTypeMismatch;
                        continue;
                    }

                    // check index range.
                    if (nodeToWrite.ParsedIndexRange.Count > 0)
                    {                            
                        // check index range for scalars.
                        if (typeInfo.ValueRank < 0)
                        {
                            errors[ii] = StatusCodes.BadIndexRangeInvalid;
                            continue;
                        }
                            
                        // check index range for arrays.
                        else
                        {
                            Array array = (Array)valueToWrite;

                            if (nodeToWrite.ParsedIndexRange.Count != array.Length)
                            {
                                errors[ii] = StatusCodes.BadIndexRangeInvalid;
                                continue;
                            }
                        }
                    }
                            
                    // write the default value.
                    error = node.Write(nodeToWrite.AttributeId, nodeToWrite.Value);

                    if (ServiceResult.IsBad(error))
                    {
                        errors[ii] = error;
                        continue;
                    }
                }
            }
        }
               
        /// <see cref="INodeManager.HistoryUpdate" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public void HistoryUpdate(
            OperationContext            context,
            Type                        detailsType,
            IList<HistoryUpdateDetails> nodesToUpdate, 
            IList<HistoryUpdateResult>  results, 
            IList<ServiceResult>        errors) 
        {
            if (context == null)       throw new ArgumentNullException(nameof(context));
            if (nodesToUpdate == null) throw new ArgumentNullException(nameof(nodesToUpdate));
            if (results == null)       throw new ArgumentNullException(nameof(results));
            if (errors == null)        throw new ArgumentNullException(nameof(errors));

            lock (m_lock)
            {
                for (int ii = 0; ii < nodesToUpdate.Count; ii++)
                {
                    HistoryUpdateDetails nodeToUpdate = nodesToUpdate[ii];

                    // skip items that have already been processed.
                    if (nodeToUpdate.Processed)
                    {
                        continue;
                    }
                    
                    // look up the node.
                    ILocalNode node = GetLocalNode(nodeToUpdate.NodeId) as ILocalNode;

                    if (node == null)
                    {
                        continue;
                    }
                    
                    // owned by this node manager.
                    nodeToUpdate.Processed = true;
                    
                    errors[ii] = StatusCodes.BadNotWritable;
                }
            }
                
        }

        /// <see cref="INodeManager.Call" />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        public void Call(
            OperationContext         context,
            IList<CallMethodRequest> methodsToCall,
            IList<CallMethodResult>  results,
            IList<ServiceResult>     errors)
        {
            if (context == null)       throw new ArgumentNullException(nameof(context));
            if (methodsToCall == null) throw new ArgumentNullException(nameof(methodsToCall));
            if (results == null)       throw new ArgumentNullException(nameof(results));
            if (errors == null)        throw new ArgumentNullException(nameof(errors));

            lock (m_lock)
            {
                for (int ii = 0; ii < methodsToCall.Count; ii++)
                {
                    CallMethodRequest methodToCall = methodsToCall[ii];

                    // skip items that have already been processed.
                    if (methodToCall.Processed)
                    {
                        continue;
                    }
                                        
                    // look up the node.
                    ILocalNode node = GetLocalNode(methodToCall.ObjectId) as ILocalNode;

                    if (node == null)
                    {
                        continue;
                    }
                    
                    methodToCall.Processed = true;                                      
                                        
                    // look up the method.
                    ILocalNode method = GetLocalNode(methodToCall.MethodId) as ILocalNode;

                    if (method == null)
                    {
                        errors[ii] = ServiceResult.Create(StatusCodes.BadMethodInvalid, "Method is not in the address space.");
                        continue;
                    }

                    // check that the method is defined for the object.
                    if (!node.References.Exists(ReferenceTypeIds.HasComponent, false, methodToCall.MethodId, true, m_server.TypeTree))
                    {
                        errors[ii] = ServiceResult.Create(StatusCodes.BadMethodInvalid, "Method is not a component of the Object.");
                        continue;
                    } 
                             
                    errors[ii] = StatusCodes.BadNotImplemented;
                }
            }
                  
        }
        
        /// <see cref="INodeManager.SubscribeToEvents" />
        public ServiceResult SubscribeToEvents(
            OperationContext    context,
            object              sourceId,
            uint                subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool                unsubscribe)
        {
            if (context == null)  throw new ArgumentNullException(nameof(context));
            if (sourceId == null) throw new ArgumentNullException(nameof(sourceId));
            if (monitoredItem == null) throw new ArgumentNullException(nameof(monitoredItem));

            lock (m_lock)
            {
                // validate the node.
                NodeMetadata metadata = GetNodeMetadata(context, sourceId, BrowseResultMask.NodeClass);

                if (metadata == null)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                // validate the node class.
                if (((metadata.NodeClass & NodeClass.Object | NodeClass.View)) == 0)
                {
                    return StatusCodes.BadNotSupported;
                }

                // check that it supports events.
                if ((metadata.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                {
                    return StatusCodes.BadNotSupported;
                }
                
                return ServiceResult.Good;
            }
        }

        /// <see cref="INodeManager.SubscribeToAllEvents" />
        public ServiceResult SubscribeToAllEvents(
            OperationContext    context,
            uint                subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool                unsubscribe)
        {  
            if (context == null)  throw new ArgumentNullException(nameof(context));
            if (monitoredItem == null)  throw new ArgumentNullException(nameof(monitoredItem));
            
            return ServiceResult.Good;
        }

        /// <see cref="INodeManager.ConditionRefresh" />
        public ServiceResult ConditionRefresh(        
            OperationContext           context,
            IList<IEventMonitoredItem> monitoredItems)
        {            
            if (context == null)  throw new ArgumentNullException(nameof(context));
            
            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a set of monitored items.
        /// </summary>
        public void CreateMonitoredItems(
            OperationContext                  context,
            uint                              subscriptionId,
            double                            publishingInterval,
            TimestampsToReturn                timestampsToReturn,
            IList<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult>              errors,
            IList<MonitoringFilterResult>     filterErrors,
            IList<IMonitoredItem>             monitoredItems,
            ref long                          globalIdCounter)
        {
            if (context == null)         throw new ArgumentNullException(nameof(context));
            if (itemsToCreate == null)   throw new ArgumentNullException(nameof(itemsToCreate));
            if (errors == null)          throw new ArgumentNullException(nameof(errors));
            if (monitoredItems == null)  throw new ArgumentNullException(nameof(monitoredItems));

            lock (m_lock)
            {
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    MonitoredItemCreateRequest itemToCreate = itemsToCreate[ii];

                    // skip items that have already been processed.
                    if (itemToCreate.Processed)
                    {
                        continue;
                    }
                    
                    // look up the node.
                    ILocalNode node = this.GetLocalNode(itemToCreate.ItemToMonitor.NodeId) as ILocalNode;

                    if (node == null)
                    {
                        continue;
                    }

                    // owned by this node manager.
                    itemToCreate.Processed = true;

                    if (!node.SupportsAttribute(itemToCreate.ItemToMonitor.AttributeId))
                    {
                        errors[ii] = StatusCodes.BadAttributeIdInvalid;
                        continue;
                    }

                    // fetch the metadata for the node.
                    NodeMetadata metadata = GetNodeMetadata(context, node, BrowseResultMask.All);

                    if (itemToCreate.ItemToMonitor.AttributeId == Attributes.Value)
                    {
                        if ((metadata.AccessLevel & AccessLevels.CurrentRead) == 0)
                        {
                            errors[ii] = StatusCodes.BadNotReadable;
                            continue;
                        }
                    }

                    // check value rank against index range.
                    if (itemToCreate.ItemToMonitor.ParsedIndexRange != NumericRange.Empty)
                    {
                        int valueRank = metadata.ValueRank;
                        
                        if (itemToCreate.ItemToMonitor.AttributeId != Attributes.Value)
                        {
                            valueRank = Attributes.GetValueRank(itemToCreate.ItemToMonitor.AttributeId);
                        }

                        if (valueRank == ValueRanks.Scalar)
                        {
                            errors[ii] = StatusCodes.BadIndexRangeInvalid;
                            continue;
                        }
                    }

                    bool rangeRequired = false;
                                        
                    // validate the filter against the node/attribute being monitored.
                    errors[ii] = ValidateFilter(
                        metadata,
                        itemToCreate.ItemToMonitor.AttributeId,
                        itemToCreate.RequestedParameters.Filter,
                        out rangeRequired);

                    if (ServiceResult.IsBad(errors[ii]))
                    {
                         continue;
                    }

                    // lookup EU range if required.
                    Range range = null;

                    if (rangeRequired)
                    {
                        errors[ii] = ReadEURange(context, node, out range);
                                                
                        if (ServiceResult.IsBad(errors[ii]))
                        {
                             continue;
                        }
                    }
                    
                    // create a globally unique identifier.
                    uint monitoredItemId = Utils.IncrementIdentifier(ref globalIdCounter);

                    // limit the sampling rate for non-value attributes.
                    double minimumSamplingInterval = m_defaultMinimumSamplingInterval;

                    if (itemToCreate.ItemToMonitor.AttributeId == Attributes.Value)
                    {
                        // use the MinimumSamplingInterval attribute to limit the sampling rate for value attributes.
                        IVariable variableNode = node as IVariable;
 
                        if (variableNode != null)
                        {
                            minimumSamplingInterval = variableNode.MinimumSamplingInterval;

                            // use the default if the node does not specify one.
                            if (minimumSamplingInterval < 0)
                            {
                                minimumSamplingInterval = m_defaultMinimumSamplingInterval;
                            }
                        }
                    }
                    
                    // create monitored item.
                    MonitoredItem monitoredItem = m_samplingGroupManager.CreateMonitoredItem(
                        context,
                        subscriptionId,
                        publishingInterval,
                        timestampsToReturn,
                        monitoredItemId,
                        node,
                        itemToCreate,
                        range,
                        minimumSamplingInterval);
                    
                    // save monitored item.
                    m_monitoredItems.Add(monitoredItem.Id, monitoredItem);

                    // update monitored item list.
                    monitoredItems[ii] = monitoredItem;
                    
                    // read the initial value.
                    DataValue initialValue = new DataValue();

                    initialValue.ServerTimestamp = DateTime.UtcNow;
                    initialValue.StatusCode      = StatusCodes.BadWaitingForInitialData;
                    
                    ServiceResult error = node.Read(context, itemToCreate.ItemToMonitor.AttributeId, initialValue);

                    if (ServiceResult.IsBad(error))
                    {
                        initialValue.Value = null;
                        initialValue.StatusCode = error.StatusCode;
                    }
                        
                    monitoredItem.QueueValue(initialValue, error);

                    // errors updating the monitoring groups will be reported in notifications.
                    errors[ii] = StatusCodes.Good;
                }
            }
 
            // update all groups with any new items.
            m_samplingGroupManager.ApplyChanges();
        }

        /// <summary>
        /// Modifies a set of monitored items.
        /// </summary>
        public void ModifyMonitoredItems(
            OperationContext                  context,
            TimestampsToReturn                timestampsToReturn,
            IList<IMonitoredItem>             monitoredItems,
            IList<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult>              errors,
            IList<MonitoringFilterResult>     filterErrors)
        { 
            if (context == null)         throw new ArgumentNullException(nameof(context));
            if (monitoredItems == null)  throw new ArgumentNullException(nameof(monitoredItems));
            if (itemsToModify == null)   throw new ArgumentNullException(nameof(itemsToModify));
            if (errors == null)          throw new ArgumentNullException(nameof(errors));

            lock (m_lock)
            {
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    MonitoredItemModifyRequest itemToModify = itemsToModify[ii];

                    // skip items that have already been processed.
                    if (itemToModify.Processed || monitoredItems[ii] == null)
                    {
                        continue;
                    }
                    
                    // check if the node manager created the item.                    
                    if (!Object.ReferenceEquals(this, monitoredItems[ii].NodeManager))
                    {
                        continue;
                    }
                                        
                    // owned by this node manager.
                    itemToModify.Processed = true;
                    
                    // validate monitored item.
                    MonitoredItem monitoredItem = null;

                    if (!m_monitoredItems.TryGetValue(monitoredItems[ii].Id, out monitoredItem))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    if (!Object.ReferenceEquals(monitoredItem, monitoredItems[ii]))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }
                    
                    // find the node being monitored.
                    ILocalNode node = monitoredItem.ManagerHandle as ILocalNode;

                    if (node == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        continue;
                    }

                    // fetch the metadata for the node.
                    NodeMetadata metadata = GetNodeMetadata(context, monitoredItem.ManagerHandle, BrowseResultMask.All);
                                        
                    bool rangeRequired = false;

                    // validate the filter against the node/attribute being monitored.
                    errors[ii] = ValidateFilter(
                        metadata,
                        monitoredItem.AttributeId,
                        itemToModify.RequestedParameters.Filter,
                        out rangeRequired);

                    if (ServiceResult.IsBad(errors[ii]))
                    {
                         continue;
                    }
                                      
                    // lookup EU range if required.
                    Range range = null;

                    if (rangeRequired)
                    {
                        // look up EU range.
                        errors[ii] = ReadEURange(context, node, out range);
                                                
                        if (ServiceResult.IsBad(errors[ii]))
                        {
                             continue;
                        }
                    }

                    // update sampling.
                    errors[ii] = m_samplingGroupManager.ModifyMonitoredItem(
                        context,
                        timestampsToReturn,
                        monitoredItem,
                        itemToModify,
                        range);
                    
                    // state of item did not change if an error returned here.
                    if (ServiceResult.IsBad(errors[ii]))
                    {
                        continue;
                    }

                    // item has been modified successfully. 
                    // errors updating the sampling groups will be reported in notifications.
                    errors[ii] = StatusCodes.Good;
                }
            }

            // update all sampling groups.
            m_samplingGroupManager.ApplyChanges();
        }

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        public void DeleteMonitoredItems(
            OperationContext      context,
            IList<IMonitoredItem> monitoredItems, 
            IList<bool>           processedItems,
            IList<ServiceResult>  errors)
        {
            if (context == null)        throw new ArgumentNullException(nameof(context));
            if (monitoredItems == null) throw new ArgumentNullException(nameof(monitoredItems));
            if (errors == null)         throw new ArgumentNullException(nameof(errors));

            lock (m_lock)
            {
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    // skip items that have already been processed.
                    if (processedItems[ii] || monitoredItems[ii] == null)
                    {
                        continue;
                    }
                    
                    // check if the node manager created the item.                    
                    if (!Object.ReferenceEquals(this, monitoredItems[ii].NodeManager))
                    {
                        continue;
                    }

                    // owned by this node manager.
                    processedItems[ii]  = true;
                    
                    // validate monitored item.
                    MonitoredItem monitoredItem = null;

                    if (!m_monitoredItems.TryGetValue(monitoredItems[ii].Id, out monitoredItem))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    if (!Object.ReferenceEquals(monitoredItem, monitoredItems[ii]))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    // remove item.
                    m_samplingGroupManager.StopMonitoring(monitoredItem);

                    // remove association with the group.
                    m_monitoredItems.Remove(monitoredItem.Id);

                    // delete successful.
                    errors[ii] = StatusCodes.Good;
                }
            }
 
            // remove all items from groups.
            m_samplingGroupManager.ApplyChanges();
        }

        /// <summary>
        /// Changes the monitoring mode for a set of monitored items.
        /// </summary>
        public void SetMonitoringMode(
            OperationContext      context,
            MonitoringMode        monitoringMode,
            IList<IMonitoredItem> monitoredItems, 
            IList<bool>           processedItems,
            IList<ServiceResult>  errors)
        {

            if (context == null)        throw new ArgumentNullException(nameof(context));
            if (monitoredItems == null) throw new ArgumentNullException(nameof(monitoredItems));
            if (errors == null)         throw new ArgumentNullException(nameof(errors));

            lock (m_lock)
            {
                for (int ii = 0; ii < errors.Count; ii++)
                {                  
                    // skip items that have already been processed.
                    if (processedItems[ii] || monitoredItems[ii] == null)
                    {
                        continue;
                    }
  
                    // check if the node manager created the item.                    
                    if (!Object.ReferenceEquals(this, monitoredItems[ii].NodeManager))
                    {
                        continue;
                    }

                    // owned by this node manager.
                    processedItems[ii]  = true;
                    
                    // validate monitored item.
                    MonitoredItem monitoredItem = null;

                    if (!m_monitoredItems.TryGetValue(monitoredItems[ii].Id, out monitoredItem))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    if (!Object.ReferenceEquals(monitoredItem, monitoredItems[ii]))
                    {
                        errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                        continue;
                    }

                    // update monitoring mode.
                    MonitoringMode previousMode = monitoredItem.SetMonitoringMode(monitoringMode);

                    // need to provide an immediate update after enabling.
                    if (previousMode == MonitoringMode.Disabled && monitoringMode != MonitoringMode.Disabled)
                    {
                        DataValue initialValue = new DataValue();

                        initialValue.ServerTimestamp = DateTime.UtcNow;
                        initialValue.StatusCode      = StatusCodes.BadWaitingForInitialData;
                        
                        // read the initial value.
                        Node node = monitoredItem.ManagerHandle as Node;
                        
                        if (node != null)
                        {
                            ServiceResult error = node.Read(context, monitoredItem.AttributeId, initialValue);

                            if (ServiceResult.IsBad(error))
                            {
                                initialValue.Value = null;
                                initialValue.StatusCode = error.StatusCode;
                            }
                        }
                                
                        monitoredItem.QueueValue(initialValue, null);
                    }
            
                    // modify the item attributes.   
                    m_samplingGroupManager.ModifyMonitoring(context, monitoredItem);
                  
                    // item has been modified successfully. 
                    // errors updating the sampling groups will be reported in notifications.
                    errors[ii] = StatusCodes.Good;
                }
            }
   
            // update all sampling groups.
            m_samplingGroupManager.ApplyChanges();
        }
        #endregion
        
        #region Static Members
        /// <summary>
        /// Returns true if the node class matches the node class mask.
        /// </summary>
        public static bool CheckNodeClassMask(uint nodeClassMask, NodeClass nodeClass)
        {
            if (nodeClassMask != 0)
            {
                return ((uint)nodeClass & nodeClassMask) != 0;
            }

            return true;
        }
        #endregion

        #region Protected Members
        /// <summary>
        /// The server that the node manager belongs to.
        /// </summary>
        protected IServerInternal Server
        {
            get { return m_server; }
        }
        #endregion
     
        #region Browsing/Searching
        /// <summary>
        /// Returns an index for the NamespaceURI (Adds it to the server namespace table if it does not already exist).
        /// </summary>
        /// <remarks>
        /// Returns the server's default index (1) if the namespaceUri is empty or null. 
        /// </remarks>
        public ushort GetNamespaceIndex(string namespaceUri)
        {
            int namespaceIndex = 1;

            if (!String.IsNullOrEmpty(namespaceUri))
            {
                namespaceIndex = m_server.NamespaceUris.GetIndex(namespaceUri);

                if (namespaceIndex == -1)
                {
                    namespaceIndex = m_server.NamespaceUris.Append(namespaceUri);
                }
            }

            return (ushort)namespaceIndex;
        }
    
        /// <summary>
        /// Returns all targets of the specified reference.
        /// </summary>
        public NodeIdCollection FindLocalNodes(NodeId sourceId, NodeId referenceTypeId, bool isInverse)
        {
            if (sourceId == null)        throw new ArgumentNullException(nameof(sourceId));
            if (referenceTypeId == null) throw new ArgumentNullException(nameof(referenceTypeId));

            lock (m_lock)
            {
                ILocalNode source = GetManagerHandle(sourceId) as ILocalNode;

                if (source == null)
                {
                    return null;
                }

                NodeIdCollection targets = new NodeIdCollection();
                
                foreach (IReference reference in source.References)
                {
                    if (reference.IsInverse != isInverse || !m_server.TypeTree.IsTypeOf(reference.ReferenceTypeId, referenceTypeId))
                    {
                        continue;
                    }

                    ExpandedNodeId targetId = reference.TargetId;

                    if (targetId.IsAbsolute)
                    {
                        continue;
                    }

                    targets.Add((NodeId)targetId);
                }
                    
                return targets;
            }
        }

        /// <summary>
        /// Returns the id the first node with the specified browse name if it exists. null otherwise
        /// </summary>
        public NodeId FindTargetId(NodeId sourceId, NodeId referenceTypeId, bool isInverse, QualifiedName browseName)
        {
            if (sourceId == null)        throw new ArgumentNullException(nameof(sourceId));
            if (referenceTypeId == null) throw new ArgumentNullException(nameof(referenceTypeId));

            lock (m_lock)
            {
                ILocalNode source = GetManagerHandle(sourceId) as ILocalNode;

                if (source == null)
                {
                    return null;
                }
                
                foreach (ReferenceNode reference in source.References)
                {
                    if (reference.IsInverse != isInverse || !m_server.TypeTree.IsTypeOf(reference.ReferenceTypeId, referenceTypeId))
                    {
                        continue;
                    }

                    ExpandedNodeId targetId = reference.TargetId;

                    if (targetId.IsAbsolute)
                    {
                        continue;
                    }

                    ILocalNode target = GetManagerHandle((NodeId)targetId) as ILocalNode;

                    if (target == null)
                    {
                        continue;
                    }
                    
                    if (QualifiedName.IsNull(browseName) || target.BrowseName == browseName)
                    {
                        return (NodeId)targetId;
                    }
                }
                    
                return null;
            }
        }            

        /// <summary>
        /// Returns the first target that matches the browse path.
        /// </summary>
        public NodeId Find(NodeId sourceId, string browsePath)
        {
            IList<NodeId> targets = TranslateBrowsePath(sourceId, browsePath);

            if (targets.Count > 0)
            {
                return targets[0];
            }

            return null;
        }

        /// <summary>
        /// Returns a list of targets the match the browse path.
        /// </summary>
        public IList<NodeId> TranslateBrowsePath(
            OperationContext context, 
            NodeId           sourceId, 
            string           browsePath)
        {
            return TranslateBrowsePath(context, sourceId, RelativePath.Parse(browsePath, m_server.TypeTree));
        }
             
        /// <summary>
        /// Returns a list of targets the match the browse path.
        /// </summary>
        public IList<NodeId> TranslateBrowsePath(
            NodeId sourceId, 
            string browsePath)
        {
            return TranslateBrowsePath(null, sourceId, RelativePath.Parse(browsePath, m_server.TypeTree));
        }
             
        /// <summary>
        /// Returns a list of targets the match the browse path.
        /// </summary>
        public IList<NodeId> TranslateBrowsePath(
            NodeId       sourceId, 
            RelativePath relativePath)
        {
            return TranslateBrowsePath(null, sourceId, relativePath);
        }
             
        /// <summary>
        /// Returns a list of targets the match the browse path.
        /// </summary>
        public IList<NodeId> TranslateBrowsePath(
            OperationContext context, 
            NodeId           sourceId, 
            RelativePath     relativePath)
        {
            List<NodeId> targets = new List<NodeId>();

            if (relativePath == null || relativePath.Elements.Count == 0)
            {
                targets.Add(sourceId);
                return targets;
            }

            // look up source in this node manager.
            ILocalNode source = null;

            lock (m_lock)
            {
                source = GetLocalNode(sourceId) as ILocalNode;

                if (source == null)
                {
                    return targets;
                }
            }

            // return the set of matching targets.
            return targets;
        }
        #endregion

        #region Registering Data/Event Sources
        /// <summary>
        /// Registers a source for a node.
        /// </summary>
        /// <remarks>
        /// The source could be one or more of IDataSource, IEventSource, ICallable, IHistorian or IViewManager
        /// </remarks>
        public void RegisterSource(NodeId nodeId, object source, object handle, bool isEventSource)
        {
            if (nodeId == null) throw new ArgumentNullException(nameof(nodeId));
            if (source == null) throw new ArgumentNullException(nameof(source));
        }   

        /// <summary>
        /// Called when the source is no longer used.
        /// </summary>
        /// <remarks>
        /// When a source disappears it must either delete all of its nodes from the address space
        /// or unregister itself their source by calling RegisterSource with source == null.
        /// After doing that the source must call this method.
        /// </remarks>
        public void UnregisterSource(object source)
        {
        }
        #endregion

        #region Adding/Removing Nodes
        
        #region Apply Modelling Rules
        /// <summary>
        /// Applys the modelling rules to any existing instance.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public void ApplyModellingRules(
            ILocalNode instance, 
            ILocalNode typeDefinition, 
            ILocalNode templateDeclaration, 
            ushort     namespaceIndex)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (typeDefinition == null) throw new ArgumentNullException(nameof(typeDefinition));

            // check existing type definition.
            UpdateTypeDefinition(instance, typeDefinition.NodeId);

            // create list of declarations for the type definition (recursively collects definitions from supertypes).
            List<DeclarationNode> declarations = new List<DeclarationNode>();
            BuildDeclarationList(typeDefinition, declarations);
            
            // add instance declaration if provided.
            if (templateDeclaration != null)
            {
                DeclarationNode declaration = new DeclarationNode();

                declaration.Node = templateDeclaration;
                declaration.BrowsePath = String.Empty;

                declarations.Add(declaration);

                BuildDeclarationList(templateDeclaration, declarations);
            }

            // build list of instances to create.
            List<ILocalNode> typeDefinitions = new List<ILocalNode>();
            SortedDictionary<string,ILocalNode> instanceDeclarations = new SortedDictionary<string,ILocalNode>();
            SortedDictionary<NodeId,ILocalNode> possibleTargets = new SortedDictionary<NodeId,ILocalNode>();
            
            // create instances from declarations.
            // subtypes appear in list last so traversing the list backwards find the overridden nodes first.
            for (int ii = declarations.Count-1; ii >= 0; ii--)
            {
                DeclarationNode declaration = declarations[ii];
                
                // update type definition list.
                if (String.IsNullOrEmpty(declaration.BrowsePath))
                {
                    typeDefinitions.Add(declaration.Node);
                    continue;
                }

                // skip declaration if instance already exists.
                // (i.e. the declaration was overridden).
                if (instanceDeclarations.ContainsKey(declaration.BrowsePath))
                {
                    continue;
                }
                
                // update instance declaration list.
                instanceDeclarations[declaration.BrowsePath] = declaration.Node;
                                        
                // save the node as a possible target of references.
                possibleTargets[declaration.Node.NodeId] = declaration.Node; 
            }
            
            // build list of instances that already exist.
            SortedDictionary<string,ILocalNode> existingInstances = new SortedDictionary<string,ILocalNode>();
            BuildInstanceList(instance, String.Empty, existingInstances);

            // maps the instance declaration onto an instance node.
            Dictionary<NodeId,ILocalNode> instancesToCreate = new Dictionary<NodeId,ILocalNode>(); 
            
            // apply modelling rules to instance declarations.
            foreach (KeyValuePair<string,ILocalNode> current in instanceDeclarations)
            {
                string browsePath = current.Key;
                ILocalNode instanceDeclaration = current.Value;

                // check if the same instance has multiple browse paths to it.
                ILocalNode newInstance = null;
                
                if (instancesToCreate.TryGetValue(instanceDeclaration.NodeId, out newInstance))
                {   
                    continue;
                }
 
                // check for an existing instance.
                if (existingInstances.TryGetValue(browsePath, out newInstance))
                {
                    continue;
                }

                // apply modelling rule to determine whether to create a new instance.
                NodeId modellingRule = instanceDeclaration.ModellingRule;
                
                // always create a new instance if one does not already exist.
                if (modellingRule == Objects.ModellingRule_Mandatory)
                {               
                    if (newInstance == null)
                    {
                        newInstance = instanceDeclaration.CreateCopy(CreateUniqueNodeId());
                        AddNode(newInstance);
                    }
                }

                // ignore optional instances unless one has been specified in the existing tree.
                else if (modellingRule == Objects.ModellingRule_Optional)
                {                            
                    if (newInstance == null)
                    {
                        continue;
                    }
                }

                // ignore any unknown modelling rules.
                else
                {
                    continue;
                }

                // save the mapping between the instance declaration and the new instance.
                instancesToCreate[instanceDeclaration.NodeId] = newInstance;
            }
            
            // add references from type definitions to top level.
            foreach (ILocalNode type in typeDefinitions)
            {
                foreach (IReference reference in type.References)
                {
                    // ignore external references from type.
                    if (reference.TargetId.IsAbsolute)
                    {
                        continue;
                    }
                    
                    // ignore subtype references.
                    if (m_nodes.TypeTree.IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasSubtype))
                    {
                        continue;                            
                    }

                    // ignore targets that are not in the instance tree.
                    ILocalNode target = null;

                    if (!instancesToCreate.TryGetValue((NodeId)reference.TargetId, out target))
                    {
                        continue;
                    }

                    // add forward and backward reference.
                    AddReference(instance, reference.ReferenceTypeId, reference.IsInverse, target, true);
                }                  
            }
           
            // add references between instance declarations.
            foreach (ILocalNode instanceDeclaration in instanceDeclarations.Values)
            {
                // find the source for the references.
                ILocalNode source = null;

                if (!instancesToCreate.TryGetValue(instanceDeclaration.NodeId, out source))
                {
                    continue;
                }
                
                // check if the source is a shared node.
                bool sharedNode = Object.ReferenceEquals(instanceDeclaration, source);

                foreach (IReference reference in instanceDeclaration.References)
                {
                    // add external reference.
                    if (reference.TargetId.IsAbsolute)
                    {
                        if (!sharedNode)
                        {
                            AddReference(source, reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                        }

                        continue;
                    }

                    // check for modelling rule.
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasModellingRule)
                    {
                        if (!source.References.Exists(ReferenceTypeIds.HasModellingRule, false, reference.TargetId, false, null))
                        {
                            AddReference(source, reference.ReferenceTypeId, false, reference.TargetId);
                        }

                        continue;                            
                    }

                    // check for type definition.
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                    {
                        if (!sharedNode)
                        {                        
                            UpdateTypeDefinition(source, instanceDeclaration.TypeDefinitionId);
                        }

                        continue;                            
                    }

                    // add targets that are not in the instance tree.
                    ILocalNode target = null;

                    if (!instancesToCreate.TryGetValue((NodeId)reference.TargetId, out target))
                    {
                        // don't update shared nodes because the reference should already exist.
                        if (sharedNode)
                        {
                            continue;
                        }

                        // top level references to the type definition node were already added.
                        if (reference.TargetId == typeDefinition.NodeId)
                        {
                            continue;
                        }

                        // see if a reference is allowed.
                        if (!IsExternalReferenceAllowed(reference.ReferenceTypeId))
                        {
                            continue;
                        }

                        // add one way reference.
                        source.References.Add(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                        continue;
                    }
                                                                    
                    // add forward and backward reference.
                    AddReference(source, reference.ReferenceTypeId, reference.IsInverse, target, true);
                }
            }
        }        

        /// <summary>
        /// Returns true if a one-way reference to external nodes is permitted.
        /// </summary>
        private bool IsExternalReferenceAllowed(NodeId referenceTypeId)
        {                        
            // always exclude hierarchial references.
            if (m_nodes.TypeTree.IsTypeOf(referenceTypeId, ReferenceTypeIds.HierarchicalReferences))
            {
                return false;
            }

            // allow one way reference to event.
            if (m_nodes.TypeTree.IsTypeOf(referenceTypeId, ReferenceTypes.GeneratesEvent))
            {
                return true;
            }

            // all other references not permitted.
            return false;
        }

        /// <summary>
        /// Updates the type definition for a node.
        /// </summary>
        private void UpdateTypeDefinition(ILocalNode instance, ExpandedNodeId typeDefinitionId)
        {
            // check existing type definition.
            ExpandedNodeId existingTypeId = instance.TypeDefinitionId;

            if (existingTypeId == typeDefinitionId)
            {
                return;
            }

            if (!NodeId.IsNull(existingTypeId))
            {
                if (m_nodes.TypeTree.IsTypeOf(existingTypeId, typeDefinitionId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeDefinitionInvalid,
                        "Type definition {0} is not a subtype of the existing type definition {1}.",
                        existingTypeId,
                        typeDefinitionId);
                }

                DeleteReference(instance, ReferenceTypeIds.HasTypeDefinition, false, existingTypeId, false);
            }

            AddReference(instance, ReferenceTypeIds.HasTypeDefinition, false, typeDefinitionId);
        }

        /// <summary>
        /// A node in the type system that is used to instantiate objects or variables.
        /// </summary>
        private class DeclarationNode
        {
            public ILocalNode Node;
            public string BrowsePath;
        }
        
        /// <summary>
        /// Builds the list of declaration nodes for a type definition. 
        /// </summary>
        private void BuildDeclarationList(ILocalNode typeDefinition, List<DeclarationNode> declarations)
        {
            if (typeDefinition == null) throw new ArgumentNullException(nameof(typeDefinition));
            if (declarations == null) throw new ArgumentNullException(nameof(declarations));

            // guard against loops (i.e. common grandparents).
            for (int ii = 0; ii < declarations.Count; ii++)
            {
                if (Object.ReferenceEquals(declarations[ii].Node, typeDefinition))
                {
                    return;
                }
            }

            // create the root declaration for the type.
            DeclarationNode declaration = new DeclarationNode();

            declaration.Node = typeDefinition;
            declaration.BrowsePath = String.Empty;

            declarations.Add(declaration);

            // follow references to supertypes first.
            foreach (IReference reference in typeDefinition.References.Find(ReferenceTypeIds.HasSubtype, true, false, null))
            {
                ILocalNode supertype = GetLocalNode(reference.TargetId) as ILocalNode;

                if (supertype == null)
                {
                    continue;
                }

                BuildDeclarationList(supertype, declarations);
            }

            // add children of type.
            BuildDeclarationList(declaration, declarations);            
        }

        /// <summary>
        /// Builds a list of declarations from the nodes aggregated by a parent.
        /// </summary>
        private void BuildDeclarationList(DeclarationNode parent, List<DeclarationNode> declarations)
        {            
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (declarations == null) throw new ArgumentNullException(nameof(declarations));

            // get list of children.
            IList<IReference> references = parent.Node.References.Find(ReferenceTypeIds.HierarchicalReferences, false, true, m_nodes.TypeTree);

            foreach (IReference reference in references)
            {
                // do not follow sub-type references.
                if (m_nodes.TypeTree.IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasSubtype))
                {
                    continue;
                }

                // find child (ignore children that are not in the node table).
                ILocalNode child = GetLocalNode(reference.TargetId) as ILocalNode;

                if (child == null)
                {
                    continue;
                }

                // create the declartion node.
                DeclarationNode declaration = new DeclarationNode();

                declaration.Node = child;
                declaration.BrowsePath = Utils.Format("{0}.{1}", parent.BrowsePath, child.BrowseName);

                declarations.Add(declaration);

                // recursively include aggregated children.
                NodeId modellingRule = child.ModellingRule;

                if (modellingRule == Objects.ModellingRule_Mandatory || modellingRule == Objects.ModellingRule_Optional)
                {
                    BuildDeclarationList(declaration, declarations);
                }
            }
        }
        
        /// <summary>
        /// Builds a table of instances indexed by browse path from the nodes aggregated by a parent
        /// </summary>
        private void BuildInstanceList(ILocalNode parent, string browsePath, IDictionary<string,ILocalNode> instances)
        { 
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            
            // guard against loops.
            if (instances.ContainsKey(browsePath))
            {
                return;
            }

            // index parent by browse path.    
            instances[browsePath] = parent;

            // get list of children.
            IList<IReference> references = parent.References.Find(ReferenceTypeIds.HierarchicalReferences, false, true, m_nodes.TypeTree);

            foreach (IReference reference in references)
            {
                // find child (ignore children that are not in the node table).
                ILocalNode child = GetLocalNode(reference.TargetId) as ILocalNode;

                if (child == null)
                {
                    continue;
                }
                
                // recursively include aggregated children.
                BuildInstanceList(child, Utils.Format("{0}.{1}", browsePath, child.BrowseName), instances);
            }
        }
        #endregion

        /// <summary>
        /// Exports a node to a nodeset.
        /// </summary>
        public void ExportNode(NodeId nodeId, NodeSet nodeSet)
        {
            lock (m_lock)
            {
                ILocalNode node = GetLocalNode(nodeId) as ILocalNode;

                if (node == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNodeIdUnknown, "NodeId ({0}) does not exist.", nodeId);
                }

                ExportNode(node, nodeSet, (node.NodeClass & (NodeClass.Object | NodeClass.Variable)) != 0);
            }
        }

        /// <summary>
        /// Exports a node to a nodeset.
        /// </summary>
        public void ExportNode(ILocalNode node, NodeSet nodeSet, bool instance)
        {
            lock (m_lock)
            {
                // check if the node has already been added.
                NodeId exportedId = nodeSet.Export(node.NodeId, m_nodes.NamespaceUris);

                if (nodeSet.Contains(exportedId))
                {
                    return;
                }

                // add to nodeset.
                Node nodeToExport = nodeSet.Add(node, m_nodes.NamespaceUris, m_nodes.ServerUris);

                // follow children.
                foreach (ReferenceNode reference in node.References)
                {
                    // export all references.
                    bool export = true;

                    // unless it is a subtype reference.
                    if (m_server.TypeTree.IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasSubtype))
                    {
                        export = false;
                    }
       
                    if (export)
                    {
                        nodeSet.AddReference(nodeToExport, reference, m_nodes.NamespaceUris, m_nodes.ServerUris);
                    }
                    
                    if (reference.IsInverse || m_server.TypeTree.IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.HasSubtype))
                    {
                        nodeSet.AddReference(nodeToExport, reference, m_nodes.NamespaceUris, m_nodes.ServerUris);
                    }

                    if (m_server.TypeTree.IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.Aggregates))
                    {
                        if (reference.IsInverse)
                        {
                            continue;
                        }
                        
                        ILocalNode child = GetLocalNode(reference.TargetId) as ILocalNode;

                        if (child != null)
                        {
                            if (instance)
                            {
                                NodeId modellingRule = child.ModellingRule;

                                if (modellingRule != Objects.ModellingRule_Mandatory)
                                {
                                    continue;
                                }
                            }

                            ExportNode(child, nodeSet, instance);
                        }
                    }
                }
            }
        }
        
        #if XXX
        /// <summary>
        /// Changes the type definition for an instance.
        /// </summary>
        public void ChangeTypeDefinition(
            NodeId instanceId,
            NodeId typeDefinitionId)
        {
            try
            {
                m_lock.Enter();

                // find the instance.                
                ILocalNode instance = GetLocalNode(instanceId) as ILocalNode;

                if (instance == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNodeIdUnknown, "NodeId ({0}) does not exist.", instanceId);
                }

                // check node class.
                if (instance.NodeClass != NodeClass.Object && instance.NodeClass != NodeClass.Variable)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNodeClassInvalid, "Node (NodeClass={0}) cannot have a type definition.", instance.NodeClass);
                }

                // get current type definition.
                ExpandedNodeId existingTypeId = instance.TypeDefinitionId;

                if (existingTypeId == typeDefinitionId)
                {
                    return;
                }

                // can only change to a subtype of the existing type definition.
                if (!m_server.TypeTree.IsTypeOf(typeDefinitionId, existingTypeId))
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid, "Type definition ({0}) must be a must subtype of the existing type definition ({1}).", typeDefinitionId, existingTypeId);
                }

                // find the type definition node.
                ILocalNode typeDefinition = GetLocalNode(typeDefinitionId) as ILocalNode;

                if (typeDefinition == null)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeDefinitionInvalid, "TypeDefinitionId ({0}) does not exist.", typeDefinitionId);
                }

                // apply modelling rules.
                NodeFactory factory = new NodeFactory(m_nodes);
                IList<ILocalNode> nodesToAdd = factory.ApplyModellingRules(instance, typeDefinition.NodeId, ref m_lastId, 1);

                // add the nodes.
                foreach (Node nodeToAdd in nodesToAdd)
                {
                    AddNode(nodeToAdd);
                }
            }
            finally
            {
                m_lock.Exit();
            }
        }
        #endif
        
        /// <summary>
        /// Updates the attributes for the node.
        /// </summary>
        private static void UpdateAttributes(ILocalNode node, NodeAttributes attributes)
        {
            // DisplayName
            if (attributes != null && (attributes.SpecifiedAttributes & (uint)NodeAttributesMask.DisplayName) != 0)
            {
                node.DisplayName = attributes.DisplayName;
            
                if (node.DisplayName == null)
                {
                    node.DisplayName = new LocalizedText(node.BrowseName.Name);
                }
            }
            else
            {
                node.DisplayName = new LocalizedText(node.BrowseName.Name);
            }
            
            // Description
            if (attributes != null && (attributes.SpecifiedAttributes & (uint)NodeAttributesMask.Description) != 0)
            {
                node.Description = attributes.Description;
            }
                     
            // WriteMask
            if (attributes != null && (attributes.SpecifiedAttributes & (uint)NodeAttributesMask.WriteMask) != 0)
            {
                node.WriteMask = (AttributeWriteMask)attributes.WriteMask;
            }
            else
            {
                node.WriteMask = AttributeWriteMask.None;
            }
                    
            // WriteMask    
            if (attributes != null && (attributes.SpecifiedAttributes & (uint)NodeAttributesMask.UserWriteMask) != 0)
            {
                node.UserWriteMask = (AttributeWriteMask)attributes.UserWriteMask;
            }
            else
            {
                node.UserWriteMask = AttributeWriteMask.None;
            }
        }
        
        /// <summary>
        /// Deletes a node from the address sapce.
        /// </summary>
        public void DeleteNode(NodeId nodeId, bool deleteChildren, bool silent)
        {
            if (nodeId == null) throw new ArgumentNullException(nameof(nodeId));
            
            // find the node to delete.
            ILocalNode node = GetManagerHandle(nodeId) as ILocalNode;

            if (node == null)
            {
                if (!silent)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSourceNodeIdInvalid, "Node '{0}' does not exist.", nodeId);
                }

                return;
            }

            bool instance = (node.NodeClass & (NodeClass.Object | NodeClass.Variable)) != 0;

            Dictionary<NodeId,IList<IReference>> referencesToDelete = new Dictionary<NodeId,IList<IReference>>();
            
            if (silent)
            {
                try
                {
                    DeleteNode(node, deleteChildren, instance, referencesToDelete);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Error deleting node: {0}", nodeId);
                }
            }
            else
            {
                DeleteNode(node, deleteChildren, instance, referencesToDelete);
            }

            if (referencesToDelete.Count > 0)
            {
                Task.Run(() =>
                {
                    OnDeleteReferences(referencesToDelete);
                });
            }
        }       

        /// <summary>
        /// Deletes a node from the address sapce.
        /// </summary>
        private void DeleteNode(ILocalNode node, bool deleteChildren, bool instance, Dictionary<NodeId,IList<IReference>> referencesToDelete)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            List<ILocalNode> nodesToDelete = new List<ILocalNode>();
            List<IReference> referencesForNode = new List<IReference>();

            lock (m_lock)
            {
                // remove the node.
                m_nodes.Remove(node.NodeId);
                    
                // check need to connect subtypes to the supertype if they are being deleted.
                ExpandedNodeId supertypeId = m_server.TypeTree.FindSuperType(node.NodeId);
                
                if (!NodeId.IsNull(supertypeId))
                {
                    m_server.TypeTree.Remove(node.NodeId);
                }

                // delete sources.
                #if LEGACY_NODEMANAGER
                DeleteRegisteredSources(node);
                #endif
                
                // remove any references to the node.
                foreach (IReference reference in node.References)
                {
                    // ignore remote references.
                    if (reference.TargetId.IsAbsolute)
                    {
                        continue;
                    }

                    // find the target.
                    ILocalNode target = GetManagerHandle(reference.TargetId) as ILocalNode;

                    if (target == null)
                    {
                        referencesForNode.Add(reference);
                        continue;
                    }
                    
                    // delete the backward reference.
                    target.References.Remove(reference.ReferenceTypeId, !reference.IsInverse, node.NodeId);

                    // check for children that need to be deleted.
                    if (deleteChildren)
                    {
                        if (m_server.TypeTree.IsTypeOf(reference.ReferenceTypeId, ReferenceTypeIds.Aggregates) && !reference.IsInverse)
                        {
                            nodesToDelete.Add(target);
                        }
                    }
                }

                if (referencesForNode.Count > 0)
                {
                    referencesToDelete[node.NodeId] = referencesForNode;
                }
            }

            // delete the child nodes.
            foreach (ILocalNode nodeToDelete in nodesToDelete)
            {
                DeleteNode(nodeToDelete, deleteChildren, instance, referencesToDelete);
            }
        }         

        /// <summary>
        /// Deletes the external references to a node in a background thread.
        /// </summary>
        private void OnDeleteReferences(object state)
        {            
            Dictionary<NodeId,IList<IReference>> referencesToDelete = state as Dictionary<NodeId,IList<IReference>>;

            if (state == null)
            {
                return;
            }
            
            foreach (KeyValuePair<NodeId,IList<IReference>> current in referencesToDelete)
            {
                try
                {
                    m_server.NodeManager.DeleteReferences(current.Key, current.Value);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Error deleting references for node: {0}", current.Key);
                }
            }            
        }

        #region Add/Remove Node Support Functions
        /// <summary>
        /// Verifies that the source and the target meet the restrictions imposed by the reference type.
        /// </summary>
        private void ValidateReference(
            ILocalNode source,
            NodeId     referenceTypeId,
            bool       isInverse,
            NodeClass  targetNodeClass)
        {
            // find reference type.
            IReferenceType referenceType = GetLocalNode(referenceTypeId) as IReferenceType;

            if (referenceType == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadReferenceTypeIdInvalid, "Reference type '{0}' does not exist.", referenceTypeId);
            }

            // swap the source and target for inverse references.
            NodeClass sourceNodeClass = source.NodeClass;

            if (isInverse)
            {
                sourceNodeClass = targetNodeClass;
                targetNodeClass = source.NodeClass;
            }

            // check HasComponent references.
            if (m_server.TypeTree.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasComponent))
            {
                if ((sourceNodeClass & (NodeClass.Object | NodeClass.Variable | NodeClass.ObjectType | NodeClass.VariableType)) == 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadReferenceNotAllowed, "Source node cannot be used with HasComponent references.");
                }

                if ((targetNodeClass & (NodeClass.Object | NodeClass.Variable | NodeClass.Method)) == 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadReferenceNotAllowed, "Target node cannot be used with HasComponent references.");
                }
                
                if (targetNodeClass == NodeClass.Variable)
                {
                    if ((targetNodeClass & (NodeClass.Variable | NodeClass.VariableType)) == 0)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadReferenceNotAllowed, "A Variable must be a component of an Variable or VariableType.");
                    }
                }

                if (targetNodeClass == NodeClass.Method)
                {
                    if ((sourceNodeClass & (NodeClass.Object | NodeClass.ObjectType)) == 0)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadReferenceNotAllowed, "A Method must be a component of an Object or ObjectType.");
                    }
                }
            }
            
            // check HasProperty references.
            if (m_server.TypeTree.IsTypeOf(referenceTypeId, ReferenceTypes.HasProperty))
            {
                if (targetNodeClass != NodeClass.Variable)
                {
                    throw ServiceResultException.Create(StatusCodes.BadReferenceNotAllowed, "Targets of HasProperty references must be Variables.");
                }                
            }

            // check HasSubtype references.
            if (m_server.TypeTree.IsTypeOf(referenceTypeId, ReferenceTypeIds.HasSubtype))
            {
                if ((sourceNodeClass & (NodeClass.DataType | NodeClass.ReferenceType | NodeClass.ObjectType | NodeClass.VariableType)) == 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadReferenceNotAllowed, "Source node cannot be used with HasSubtype references.");
                }

                if (targetNodeClass != sourceNodeClass)
                {
                    throw ServiceResultException.Create(StatusCodes.BadReferenceNotAllowed, "The source and target cannot be connected by a HasSubtype reference.");
                }
            }                      

            // TBD - check rules for other reference types.
        }
        #endregion
        #endregion
        
        #region Adding/Removing References
        /// <summary>
        /// Adds a reference between two existing nodes.
        /// </summary>
        public ServiceResult AddReference(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool   isInverse,
            NodeId targetId,
            bool   bidirectional)
        {
            lock (m_lock)
            {
                // find source.
                ILocalNode source = GetManagerHandle(sourceId) as ILocalNode;

                if (source == null)
                {
                    return StatusCodes.BadParentNodeIdInvalid;
                }
                                
                // add reference from target to source.      
                if (bidirectional)
                {              
                    // find target.
                    ILocalNode target = GetManagerHandle(targetId) as ILocalNode;

                    if (target == null)
                    {
                        return StatusCodes.BadNodeIdUnknown;
                    }
                 
                    // ensure the reference is valid.
                    ValidateReference(source, referenceTypeId, isInverse, target.NodeClass);

                    // add reference from target to source.
                    AddReferenceToLocalNode(target, referenceTypeId, !isInverse, sourceId, false);
                }
                
                // add reference from source to target.                
                AddReferenceToLocalNode(source, referenceTypeId, isInverse, targetId, false);

                return null;
            }
        }

        /// <summary>
        /// Ensures any changes to built-in nodes are reflected in the diagnostics node manager.
        /// </summary>
        private void AddReferenceToLocalNode(
            ILocalNode     source,
            NodeId         referenceTypeId,
            bool           isInverse,
            ExpandedNodeId targetId,
            bool isInternal)
        {
            source.References.Add(referenceTypeId, isInverse, targetId);

            if (!isInternal && source.NodeId.NamespaceIndex == 0)
            {
                lock (Server.DiagnosticsNodeManager.Lock)
                {
                    NodeState state = Server.DiagnosticsNodeManager.FindPredefinedNode(source.NodeId, null);

                    if (state != null)
                    {
                        INodeBrowser browser = state.CreateBrowser(
                            m_server.DefaultSystemContext,
                            null,
                            referenceTypeId,
                            true,
                            (isInverse) ? BrowseDirection.Inverse : BrowseDirection.Forward,
                            null,
                            null,
                            true);

                        bool found = false;

                        for (IReference reference = browser.Next(); reference != null; reference = browser.Next())
                        {
                            if (reference.TargetId == targetId)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            state.AddReference(referenceTypeId, isInverse, targetId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds a reference between two existing nodes.
        /// </summary>
        public void CreateReference(
            NodeId       sourceId,
            NodeId       referenceTypeId,
            bool         isInverse,
            NodeId       targetId,
            bool         bidirectional)
        {
            lock (m_lock)
            {
                ServiceResult result = AddReference(sourceId, referenceTypeId, isInverse, targetId, bidirectional);

                if (ServiceResult.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }
            }
        }

        /// <summary>
        /// Adds a reference to the address space.
        /// </summary>
        private void AddReference(
            ILocalNode source, 
            NodeId     referenceTypeId, 
            bool       isInverse, 
            ILocalNode target, 
            bool       bidirectional)
        {
            AddReferenceToLocalNode(source, referenceTypeId, isInverse, target.NodeId, false);
            
            if (bidirectional)
            {
                AddReferenceToLocalNode(target, referenceTypeId, !isInverse, source.NodeId, false);
            }            
        }        

        /// <summary>
        /// Adds a reference to the address space.
        /// </summary>
        private void AddReference(
            ILocalNode     source, 
            NodeId         referenceTypeId, 
            bool           isInverse, 
            ExpandedNodeId targetId)
        {
            AddReferenceToLocalNode(source, referenceTypeId, isInverse, targetId, false);
        }

        /// <summary>
        /// Deletes a reference.
        /// </summary>
        public ServiceResult DeleteReference(
            object         sourceHandle, 
            NodeId         referenceTypeId,
            bool           isInverse, 
            ExpandedNodeId targetId, 
            bool           deleteBidirectional)
        {
            if (sourceHandle == null)    throw new ArgumentNullException(nameof(sourceHandle));
            if (referenceTypeId == null) throw new ArgumentNullException(nameof(referenceTypeId));
            if (targetId == null)        throw new ArgumentNullException(nameof(targetId));

            lock (m_lock)
            {
                ILocalNode source = sourceHandle as ILocalNode;

                if (source == null)
                {
                    return StatusCodes.BadSourceNodeIdInvalid;
                }

                source.References.Remove(referenceTypeId, isInverse, targetId);

                if (deleteBidirectional)
                {
                    ILocalNode target = GetManagerHandle(targetId) as ILocalNode;

                    if (target != null)
                    {
                        target.References.Remove(referenceTypeId, !isInverse, source.NodeId);
                    }
                }
                   
                return ServiceResult.Good;
            }
        }
        
        /// <summary>
        /// Deletes a reference.
        /// </summary>
        public void DeleteReference(
            NodeId sourceId, 
            NodeId referenceTypeId, 
            bool isInverse, 
            ExpandedNodeId targetId, 
            bool deleteBidirectional)
        { 
            ServiceResult result = DeleteReference(
                GetManagerHandle(sourceId) as ILocalNode, 
                referenceTypeId, 
                isInverse, 
                targetId, 
                deleteBidirectional);

            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }
        }

        /// <summary>
        /// Adds a node to the address space.
        /// </summary>
        private void AddNode(ILocalNode node)
        {            
            m_nodes.Attach(node);
        }
        #endregion
        
        /// <summary>
        /// Returns a node managed by the manager with the specified node id.
        /// </summary>
        public ILocalNode GetLocalNode(ExpandedNodeId nodeId)
        {
            if (nodeId == null)
            {
                return null;
            }

            // check for absolute declarations of local nodes.
            if (nodeId.IsAbsolute)
            {
                if (nodeId.ServerIndex != 0)
                {
                    return null;
                }
                 
                int namespaceIndex = this.Server.NamespaceUris.GetIndex(nodeId.NamespaceUri);
                
                if (namespaceIndex < 0 || nodeId.NamespaceIndex >= this.Server.NamespaceUris.Count)
                {
                    return null;
                }

                return GetLocalNode(new NodeId(nodeId.Identifier, (ushort)namespaceIndex));
            }

            return GetLocalNode((NodeId)nodeId);
        }
        
        /// <summary>
        /// Returns a node managed by the manager with the specified node id.
        /// </summary>
        public ILocalNode GetLocalNode(
            NodeId nodeId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName)
        {
            lock (m_lock)
            {
                return m_nodes.Find(nodeId, referenceTypeId, isInverse, includeSubtypes, browseName) as ILocalNode;
            }
        }

        /// <summary>
        /// Returns a node managed by the manager with the specified node id.
        /// </summary>
        public ILocalNode GetLocalNode(NodeId nodeId)
        {
            lock (m_lock)
            {
                return m_nodes.Find(nodeId) as ILocalNode;
            }
        }

        /// <summary>
        /// Returns a list of nodes which are targets of the specified references.
        /// </summary>
        public IList<ILocalNode> GetLocalNodes(
            NodeId        sourceId, 
            NodeId        referenceTypeId,
            bool          isInverse, 
            bool          includeSubtypes)
        {
            lock (m_lock)
            {
                List<ILocalNode> targets = new List<ILocalNode>();

                ILocalNode source = GetLocalNode(sourceId) as ILocalNode;

                if (source == null)
                {
                    return targets;
                }

                foreach (IReference reference in source.References.Find(referenceTypeId, isInverse, true, m_nodes.TypeTree))
                {                    
                    ILocalNode target = GetLocalNode(reference.TargetId) as ILocalNode;

                    if (target != null)
                    {
                        targets.Add(target);
                    }
                }

                return targets;
            }
        }
        
        /// <summary>
        /// Returns a node managed by the manager that has the specified browse name.
        /// </summary>
        public ILocalNode GetTargetNode(
            NodeId        sourceId, 
            NodeId        referenceTypeId,
            bool          isInverse, 
            bool          includeSubtypes, 
            QualifiedName browseName)
        {
            lock (m_lock)
            {
                ILocalNode source = GetLocalNode(sourceId) as ILocalNode;

                if (source == null)
                {
                    return null;
                }

                return GetTargetNode(source, referenceTypeId, isInverse, includeSubtypes, browseName);
            }
        }
        
        /// <summary>
        /// Returns a node managed by the manager that has the specified browse name.
        /// </summary>
        private ILocalNode GetTargetNode(
            ILocalNode    source, 
            NodeId        referenceTypeId,
            bool          isInverse, 
            bool          includeSubtypes, 
            QualifiedName browseName)
        {
            foreach (IReference reference in source.References.Find(referenceTypeId, isInverse, includeSubtypes, m_server.TypeTree))
            {
                ILocalNode target = GetLocalNode(reference.TargetId) as ILocalNode;

                if (target == null)
                {
                    continue;
                }

                if (QualifiedName.IsNull(browseName) || browseName == target.BrowseName)
                {
                    return target;
                }
            }

            return null;
        }
        /// <summary>
        /// Attaches a node to the address space.
        /// </summary>
        public void AttachNode(ILocalNode node)
        {
            AttachNode(node, false);
        }

        /// <summary>
        /// Attaches a node to the address space.
        /// </summary>
        private void AttachNode(ILocalNode node, bool isInternal)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            lock (m_lock)
            {
                // check if node exists.
                if (m_nodes.Exists(node.NodeId))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdExists, 
                        "A node with the same node id already exists: {0}",
                        node.NodeId);
                }

                // ensure reverse references exist.
                foreach (IReference reference in node.References)
                {
                    // ignore references that are always one way.
                    if (reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition || reference.ReferenceTypeId == ReferenceTypeIds.HasModellingRule)
                    {
                        continue;
                    }

                    // find target.
                    ILocalNode target = GetLocalNode(reference.TargetId) as ILocalNode;

                    if (target != null)
                    {
                        AddReferenceToLocalNode(target, reference.ReferenceTypeId, !reference.IsInverse, node.NodeId, isInternal);
                    }                  
                }
                
                // must generate a model change event.
                AddNode(node);
            }
        }
        
        /// <summary>
        /// Creates a unique node identifier.
        /// </summary>
        public NodeId CreateUniqueNodeId()
        {
            return CreateUniqueNodeId(m_dynamicNamespaceIndex);
        }
        
        #region Private Methods
        /// <see cref="INodeManager.GetManagerHandle" />
        private object GetManagerHandle(ExpandedNodeId nodeId)
        {
            lock (m_lock)
            {
                if (nodeId == null || nodeId.IsAbsolute)
                {
                    return null;
                }

                return GetLocalNode(nodeId) as ILocalNode;
            }
        }        

        #if LEGACY_CORENODEMANAGER
        /// <summary>
        /// Checks if the operation needs to be handled by an external source.
        /// </summary>
        private static bool CheckSourceHandle(ILocalNode node, Type sourceType, int index, IDictionary sources)
        {            
            // check if a source is defined for the node.
            SourceHandle handle = node.Handle as SourceHandle;

            if (handle == null)
            {
                return false;
            }

            // check if the source type is valid.
            if (!sourceType.IsInstanceOfType(handle.Source))
            {
                return false;
            }

            // find list of handles for the source.
            List<RequestHandle> handles = null;

            if (!sources.Contains(handle.Source))
            {
                sources[handle.Source] = handles = new List<RequestHandle>();
            }
            else
            {
                handles = (List<RequestHandle>)sources[handle.Source];
            }

            // add node to list of values to process by the source.
            handles.Add(new RequestHandle(handle.Handle, index));

            return true;
        }

        /// <summary>
        /// Recursively subscribes to events for the notifiers in the tree.
        /// </summary>
        private void SubscribeToEvents(
            OperationContext    context,
            ILocalNode          node,
            uint                subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool                unsubscribe)
        {
            // find handle associated with the node.
            IEventSource eventSource = node as IEventSource;
            SourceHandle handle = node.Handle as SourceHandle;

            if (handle != null)
            {
                eventSource = handle.Source as IEventSource;            
            }
            
            if (eventSource != null)
            {
                try
                {
                    eventSource.SubscribeToEvents(context, (handle != null)?handle.Handle:null, subscriptionId, monitoredItem, unsubscribe);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error calling SubscribeToEvents on an EventSource.");
                }
            }
    
            // find the child notifiers.
            IList<IReference> references = node.References.Find(ReferenceTypes.HasNotifier, false, true, m_server.TypeTree);

            for (int ii = 0; ii < references.Count; ii++)
            {
                if (!references[ii].TargetId.IsAbsolute)
                {
                    ILocalNode target = GetManagerHandle(references[ii].TargetId) as ILocalNode;

                    if (target == null)
                    {
                        continue;
                    }
                    
                    // only object or views can produce events.
                    if ((target.NodeClass & (NodeClass.Object | NodeClass.View)) == 0)
                    {
                        continue;
                    }

                    SubscribeToEvents(context, target, subscriptionId, monitoredItem, unsubscribe);
                }
            }       
        }
        #endif
        
        /// <summary>
        /// Reads the EU Range for a variable.
        /// </summary>
        private ServiceResult ReadEURange(OperationContext context, ILocalNode node, out Range range)
        {
            range = null;

            IVariable target = GetTargetNode(node, ReferenceTypes.HasProperty, false, true, BrowseNames.EURange) as IVariable;

            if (target == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            range = target.Value as Range;
            
            if (range == null)
            {
                return StatusCodes.BadTypeMismatch;
            }

            return ServiceResult.Good;
        }
        
        /// <summary>
        /// Validates a filter for a monitored item.
        /// </summary>
        private ServiceResult ValidateFilter(
            NodeMetadata    metadata, 
            uint            attributeId, 
            ExtensionObject filter, 
            out bool        rangeRequired)
        {
            rangeRequired = false;

            // check filter.
            DataChangeFilter datachangeFilter = null;

            if (filter != null)
            {
                datachangeFilter = filter.Body as DataChangeFilter;
            }
            
            if (datachangeFilter != null)
            {
                // get the datatype of the node.                
                NodeId datatypeId = metadata.DataType;
                
                // check that filter is valid.
                ServiceResult error = datachangeFilter.Validate();

                if (ServiceResult.IsBad(error))
                {
                    return error;
                }
                
                // check datatype of the variable.
                if (!m_server.TypeTree.IsTypeOf(datatypeId, DataTypes.Number))
                {
                    return StatusCodes.BadDeadbandFilterInvalid;
                }

                // percent deadbands only allowed for analog data items.
                if (datachangeFilter.DeadbandType == (uint)(int)DeadbandType.Percent)
                {
                    ExpandedNodeId typeDefinitionId = metadata.TypeDefinition;

                    if (typeDefinitionId == null)
                    {
                        return StatusCodes.BadDeadbandFilterInvalid;
                    }
                    
                    // percent deadbands only allowed for analog data items.
                    if (!m_server.TypeTree.IsTypeOf(typeDefinitionId, VariableTypes.AnalogItemType))
                    {
                        return StatusCodes.BadDeadbandFilterInvalid;
                    }

                    // the EURange property is required to use the filter.
                    rangeRequired = true;
                }
            }
        
            // filter is valid
            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a new unique identifier for a node.
        /// </summary>
        private NodeId CreateUniqueNodeId(ushort namespaceIndex)
        {
            return new NodeId(Utils.IncrementIdentifier(ref m_lastId), namespaceIndex);
        }
        #endregion
        
        #region Private Fields
        private IServerInternal m_server;
        private object m_lock = new object();
        private NodeTable m_nodes;
        private long m_lastId;
        private SamplingGroupManager m_samplingGroupManager;
        private Dictionary<uint, MonitoredItem> m_monitoredItems;
        
        #if LEGACY_CORENODEMANAGER
        private Dictionary<object,IEventSource> m_eventSources;
        #endif
        
        private double m_defaultMinimumSamplingInterval;
        private List<string> m_namespaceUris;
        private ushort m_dynamicNamespaceIndex;
        #endregion            
    }    
    
}
