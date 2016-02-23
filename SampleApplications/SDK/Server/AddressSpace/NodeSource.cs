/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Runtime.Serialization;
using System.Reflection;
using System.Threading;

#pragma warning disable 0618

namespace Opc.Ua.Server
{       
#if LEGACY_CORENODEMANAGER
    /// <summary>
    /// The base class for custom nodes.
    /// </summary>
    [Obsolete("The NodeSource class is obsolete and is not supported. See Opc.Ua.NodeState for a replacement.")]
    public abstract class NodeSource : ILocalNode, IDisposable, IFormattable, ICloneable
    {
        #region Constructors
        /// <summary>
        /// Associates the object with a server (mandatory) and its model parent (optional).
        /// </summary>
        protected NodeSource(
            IServerInternal server,
            NodeSource      parent)
        {
            if (server == null) throw new ArgumentNullException("server");
            
            m_server     = server;
            m_parent     = parent;
            m_references = new ReferenceCollection();
            
            if (parent != null)
            {
                m_lock = parent.m_lock;
                parent.AddChild(this);
            }
        }
        #endregion        
        
		#region IDisposable Members
        /// <summary>
        /// The finializer implementation.
        /// </summary>
        ~NodeSource() 
        {
            Dispose(false);
        }
        
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {   
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                List<NodeSource> children = null;

                lock (DataLock)
                {
                    m_disposed = true;

                    if (disposing)
                    {
                        lock (DataLock)
                        {
                            if (m_children != null)
                            {
                                children = new List<NodeSource>(m_children);
                                m_children.Clear();
                            }

                            m_references.Clear();
                        }
                    }
                }
                                    
                if (children != null)
                {
                    foreach (NodeSource child in children)
                    {
                        Utils.SilentDispose(child);
                    }
                }
            }
        }

        /// <summary>
        /// Whether the object has been disposed.
        /// </summary>
        public bool Disposed
        {
            get { return m_disposed; }
        }
        #endregion
                
        #region IFormattable Members
        /// <summary>
        /// Returns a string representation of the HierarchyReference.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns a string representation of the HierarchyReference.
        /// </summary>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format != null)
            {
                throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            }

            string nodeId = Utils.Format("{0}", m_nodeId);
            string nodeClass = Utils.Format("{0}", NodeClass);
            string displayName = Utils.Format("{0}", m_browseName);

            if (String.IsNullOrEmpty(displayName))
            {
                return Utils.Format("{0} {1}", nodeClass, nodeId);
            }

            return Utils.Format("{0} ({1} {2})", displayName, nodeClass, nodeId);
        }
        #endregion

        #region ICloneable Members
        /// <summary>
        /// Makes a copy of the node without any node ids specified.
        /// </summary>
        public new object MemberwiseClone()
        {
            return Clone(null);
        }

        /// <summary>
        /// Makes a copy of the node without any node ids specified.
        /// </summary>
        public abstract NodeSource Clone(NodeSource parent);
        #endregion

        #region INode Members
        /// <summary cref="INode.NodeId" />
        ExpandedNodeId INode.NodeId
        {
            get
            {
                lock (DataLock)
                {
                    return m_nodeId;
                }
            }          
        }

        /// <summary cref="INode.NodeClass" />
        public virtual NodeClass NodeClass
        {
            get
            {
                return NodeClass.Unspecified;
            }
        }

        /// <summary cref="INode.BrowseName" />
        public QualifiedName BrowseName
        {
            get
            {
                lock (DataLock)
                {
                    return m_browseName;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_browseName = value;
                }
            }
        }
        
        /// <summary cref="INode.DisplayName" />
        public LocalizedText DisplayName
        {
            get
            {
                lock (DataLock)
                {
                    if (LocalizedText.IsNullOrEmpty(m_displayName) && m_browseName != null)
                    {
                        return new LocalizedText(m_browseName.Name);
                    }

                    return m_displayName;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_displayName = value;
                }
            }
        }
        
        /// <summary cref="INode.TypeDefinitionId" />
        public virtual ExpandedNodeId TypeDefinitionId
        {
            get
            {
                return ExpandedNodeId.Null;
            }
        }
        #endregion     
        
        #region ILocalNode Members
        /// <summary cref="ILocalNode.Handle" />
        public object Handle
        {
            get
            {
                lock (DataLock)
                {
                    return m_handle;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_handle = value;
                }
            }
        }

        /// <summary cref="ILocalNode.Description" />
        public LocalizedText Description
        {
            get
            {
                lock (DataLock)
                {
                    return m_description;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_description = value;
                }
            }
        }        
        
        /// <summary cref="ILocalNode.WriteMask" />
        public AttributeWriteMask WriteMask
        {
            get
            {
                lock (DataLock)
                {
                    return m_writeMask;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_writeMask = value;
                }
            }
        }
        
        /// <summary cref="ILocalNode.UserWriteMask" />
        public AttributeWriteMask UserWriteMask
        {
            get
            {
                lock (DataLock)
                {
                    return m_userWriteMask;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_userWriteMask = value;
                }
            }
        }

        /// <summary cref="ILocalNode.ModellingRule" />
        public NodeId ModellingRule 
        { 
            get
            {
                return (NodeId)m_references.FindTarget(ReferenceTypeIds.HasModellingRule, false, false, null, 0);
            }
        }  
        
        /// <summary cref="ILocalNode.References" />
        public IReferenceCollection References
        {
            get
            {
                return m_references;
            }
        }

        /// <summary cref="ILocalNode.CreateCopy" />
        public virtual ILocalNode CreateCopy(NodeId nodeId)
        {
            Node node = Node.Copy(this);
            node.NodeId = nodeId;
            return node;
        }

        /// <summary cref="ILocalNode.SupportsAttribute" />
        public virtual bool SupportsAttribute(uint attributeId)
        {
            lock (DataLock)
            {
                switch (attributeId)
                {
                    case Attributes.NodeId:
                    case Attributes.NodeClass:
                    case Attributes.BrowseName:
                    case Attributes.DisplayName:
                    case Attributes.WriteMask:
                    case Attributes.UserWriteMask:
                    {
                        return true;
                    }

                    case Attributes.Description:
                    {
                        return !LocalizedText.IsNullOrEmpty(Description);
                    }

                    default:
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary cref="ILocalNode.Read" />
        public ServiceResult Read(IOperationContext context, uint attributeId, DataValue value)
        {
            lock (DataLock)
            {
                if (!SupportsAttribute(attributeId))
                {
                    return StatusCodes.BadAttributeIdInvalid;
                }

                ServiceResult result = ReadAttribute(context, attributeId, value);

                if (ServiceResult.IsBad(result))
                {
                    value.Value = null;
                }
                
                if (result != null)
                {
                    value.StatusCode = result.StatusCode;
                }

                return null;
            }
        }
        
        /// <summary cref="ILocalNode.Write" />
        public ServiceResult Write(uint attributeId, DataValue value)
        {
            lock (DataLock)
            {
                if (!SupportsAttribute(attributeId))
                {
                    return StatusCodes.BadAttributeIdInvalid;
                }
                
                // check for read only attributes.
                switch (attributeId)
                {
                    case Attributes.NodeId:
                    case Attributes.NodeClass:
                    {
                        return StatusCodes.BadNotWritable;
                    }
                }

                // check data type.
                if (attributeId != Attributes.Value)
                {
                    if (Attributes.GetDataTypeId(attributeId) != TypeInfo.GetDataTypeId(value))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }
                }

                // write value.
                return WriteAttribute(attributeId, value);
            }
        }
        #endregion      

        #region Public Properties
        /// <remarks>
        /// All node sources must be thread safe because the NodeManager makes calls to the objects 
        /// to service client requests. This property is used to 
        /// </remarks>
        public object DataLock
        {
            get { return m_lock; }
        }

        /// <summary>
        /// The parent for the node.
        /// </summary>
        public NodeSource Parent
        {
            get
            {
                return m_parent;
            }
        }

        /// <summary>
        /// A numeric identifier for the instance that is unique within the parent.
        /// </summary>
        /// <remarks>
        /// The BaseTypeSource.AssignIdsToChilden method can be used to set these values.
        /// </remarks>
        public uint NumericId
        {
            get
            {
                lock (DataLock)
                {
                    return m_numericId;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_numericId = value;
                }
            }
        }        
        
        /// <summary>
        /// The identifier for the node.
        /// </summary>
        public NodeId NodeId
        {
            get
            {
                lock (DataLock)
                {
                    return m_nodeId;
                }
            }

            set
            {
                lock (DataLock)
                {
                    if (m_created)
                    {
                        throw new InvalidOperationException("Cannot change the NodeId after calling Create().");
                    }

                    m_nodeId = value;
                }
            }
        }

        /// <summary>
        /// Whether the node source has been created in the NodeManager's address space.
        /// </summary>
        public bool Created
        {
            get
            {
                lock (DataLock)
                {
                    return m_created;
                }
            }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Initializes a node from another node.
        /// </summary>
        public virtual void Initialize(NodeSource source)
        {
            lock (DataLock)
            {
                m_nodeId = source.m_nodeId;
                m_handle = source.m_handle;
                m_numericId = source.m_numericId;
                m_browseName = source.m_browseName;
                m_displayName = source.m_displayName;
                m_description = source. m_description;
                m_writeMask = source.m_writeMask;
                m_userWriteMask = source.m_userWriteMask;
            }
        }

        /// <summary>
        /// Verified that the current thread has locked the NodeManager.
        /// </summary>
        protected void CheckNodeManagerState()
        { 
            if (!NodeManager.HasLock())
            {
                throw new InvalidOperationException("This method calls methods on the NodeManager which means the lock on the NodeManager must be acquired first."); 
            }
        }

        /// <summary>
        /// Updates the object based from a configuration.
        /// </summary>
        public virtual void Create(
            NodeId          parentId,
            NodeId          referenceTypeId, 
            NodeId          nodeId,
            QualifiedName   browseName, 
            uint            numericId,
            object          configuration)
        {         
            CheckNodeManagerState();

            lock (DataLock)
            {
                // update the node id.
                if (NodeId.IsNull(nodeId))
                {
                    nodeId = m_nodeId;
                }
                else
                {                        
                    m_nodeId = nodeId;
                }

                // update the browse name.
                if (QualifiedName.IsNull(browseName))
                {
                    browseName = m_browseName;
                }
                else
                {                        
                    m_browseName = browseName;

                    if (!QualifiedName.IsNull(browseName))
                    {
                        m_displayName = new LocalizedText(browseName.Name);
                    }
                }

                // save the numeric id.
                if (numericId != 0)
                {
                    m_numericId = numericId;
                }

                // do any pre-create processing.
                configuration = OnBeforeCreate(configuration);
                                    
                ILocalNode existingNode = null;

                // find the node by finding a child of the parent with the same browse path.
                if (NodeId.IsNull(this.NodeId))
                {                   
                    existingNode = NodeManager.GetTargetNode(
                        parentId,
                        referenceTypeId,
                        false,
                        true,
                        browseName);

                    // must assign a node id since one does not already exist.
                    if (existingNode == null)
                    {
                        m_nodeId = NodeManager.CreateUniqueNodeId();
                    }
                    else
                    {
                        m_nodeId = existingNode.NodeId;
                    }
                }

                // find the node with the node id provided.
                else
                {
                    existingNode = NodeManager.GetLocalNode(this.NodeId);
                }

                if (existingNode == null)
                {
                    // create the node.
                    CreateNode(parentId, referenceTypeId);

                    // get the default template created by the node manager.
                    existingNode = NodeManager.GetLocalNode(this.NodeId);
                }   

                // update the attributes.
                UpdateAttributes(existingNode);

                // update the references.
                UpdateReferences(existingNode);
                
                // apply the configuration to the node.
                ApplyConfiguration(configuration);

                // add the source to the address space.
                NodeManager.ReplaceNode(existingNode, this);
                
                // recursively configure the children.
                CreateChildren(configuration);
                
                m_created = true;

                // do any post-configuration processing.
                OnAfterCreate(configuration);
            }
        }

        /// <summary>
        /// Deletes the Node from the address space.
        /// </summary>
        /// <remarks>
        /// This method is called deletes all nodes created when the type was instantiated. The subclass
        /// must implement DeleteChildren() to remove nodes that are not part of the type model.
        /// 
        /// Before any processing starts this method calls OnBeforeDelete().
        /// After successfully deleting all nodes this method calls OnAfterDelete(). 
        /// </remarks>
        public void Delete()
        {               
            CheckNodeManagerState();

            lock (DataLock)
            {
                // do any pre-delete processing.
                OnBeforeDelete();
                
                // delete children.
                DeleteChildren();

                // delete the node.
                NodeManager.DeleteNode(m_nodeId, true, true);

                // do any post-delete processing.
                OnAfterDelete();

                // clear the node to indicate the object is no longer valid.
                m_nodeId = null;
            }
        }
        
        /// <summary>
        /// Recursively overrides the status for value of any child variables.
        /// </summary>
        public virtual void OverrideValueStatus(StatusCode statusCode)
        {
            lock (DataLock)
            {
                if (m_children != null)
                {
                    foreach (NodeSource child in m_children)
                    {
                        child.OverrideValueStatus(statusCode);
                    }
                }
            }
        }
        
        /// <summary>
        /// Builds a path of display names starting with the top most parent.
        /// </summary>
        public LocalizedText GetDisplayPath(int count)
        {            
            // collect the display names.
            Stack<LocalizedText> displayNames = new Stack<LocalizedText>();

            NodeSource parent = this;

            while (parent != null)
            {                
                displayNames.Push(parent.DisplayName);
                parent = parent.Parent;

                if (count > 0 && count <= displayNames.Count)
                {
                    break;
                }
            }
            
            // construct a string with '/' seperators between the names.
            StringBuilder displayPath = new StringBuilder();

            while (displayNames.Count > 0)
            {
                if (displayPath.Length > 0)
                {
                    displayPath.Append('/');
                }

                displayPath.AppendFormat("{0}", displayNames.Pop());
            }

            return displayPath.ToString();
        }

        /// <summary>
        /// Adds a subscription to the source.
        /// </summary>
        public virtual void Subscribe(IMonitoredItem monitoredItem)
        {
            lock (DataLock)
            {
                if (m_monitoredItems == null)
                {
                    m_monitoredItems = new Dictionary<uint,IMonitoredItem>();
                }
                    
                m_monitoredItems[monitoredItem.Id] = monitoredItem;
            }
        }

        /// <summary>
        /// Removes a subscription from the source.
        /// </summary>
        public virtual void Unsubscribe(IMonitoredItem monitoredItem)
        {
            lock (DataLock)
            {
                if (m_monitoredItems != null)
                {
                    m_monitoredItems.Remove(monitoredItem.Id);
                }                    
            }
        }
        #endregion

        #region Protected Members
        /// <summary>
        /// Returns the index for the namespace uri (adds it to the table if it does not already exist).
        /// </summary>
        protected ushort GetNamespaceIndex(string namespaceUri)
        {
            return m_server.NamespaceUris.GetIndexOrAppend(namespaceUri);
        }

        /// <summary>
        /// Returns the Server that the node belongs to.
        /// </summary>
        protected IServerInternal Server
        {
            get { return m_server; } 
        }

        /// <summary>
        /// Returns the table of namespaces used by the server.
        /// </summary>
        protected NamespaceTable NamespaceUris
        {
            get { return m_server.NamespaceUris; } 
        }

        /// <summary>
        /// Returns the table of servers known to the server.
        /// </summary>
        protected StringTable ServerUris
        {
            get { return m_server.ServerUris; } 
        }
        
        /// <summary>
        /// Returns the resource manager to use when localizing strings.
        /// </summary>
        protected ResourceManager ResourceManager
        {
            get { return m_server.ResourceManager; } 
        }
        
        /// <summary>
        /// Returns the event manager to use when reporting events.
        /// </summary>
        protected EventManager EventManager
        {
            get { return m_server.EventManager; } 
        }

        /// <summary>
        /// Returns the NodeManager that the node belongs to.
        /// </summary>
        protected CoreNodeManager NodeManager
        {
            get { return m_server.CoreNodeManager; } 
        }
        
        /// <summary>
        /// Finds the node identified by the relative path.
        /// </summary>
        protected virtual NodeSource Find(IList<QualifiedName> relativePath, int index)
        {
            // check for end of path.
            if (index < 0 || index >= relativePath.Count)
            {
                return null;
            }

            QualifiedName browseName = relativePath[index];
            
            // follow forward references to child.
            foreach (NodeSource child in m_children)
            {
                if (child.BrowseName != browseName)
                {
                    continue;
                }

                if (index == relativePath.Count-1)
                {
                    return child;
                }

                return child.Find(relativePath, ++index);
            }
                    
            // nothing found.
            return null;
        }

        /// <summary>
        /// Returns the children of the source.
        /// </summary>
        protected IEnumerable<NodeSource> Children
        {
            get 
            {
                lock (DataLock)
                {
                    return m_children;
                }
            }
        }

        /// <summary>
        /// Returns any active monitored items.
        /// </summary>
        protected IEnumerable<IMonitoredItem> MonitoredItems
        {
            get 
            {
                lock (DataLock)
                {
                    if (m_monitoredItems != null)
                    {
                        return m_monitoredItems.Values;
                    }

                    return null;
                }
            }
        }

        /// <summary>
        /// Returns true if the node is being monitored.
        /// </summary>
        protected bool HasMonitoredItems
        {
            get 
            {
                lock (DataLock)
                {
                    return m_monitoredItems != null && m_monitoredItems.Count > 0;
                }
            }
        }

        /// <summary>
        /// Returns the specified monitored item. Null if it does not exist.
        /// </summary>
        protected IMonitoredItem FindMonitoredItem(uint id)
        {
            lock (DataLock)
            {
                if (m_monitoredItems != null)
                {
                    IMonitoredItem monitoredItem = null;

                    if (m_monitoredItems.TryGetValue(id, out monitoredItem))
                    {
                        return monitoredItem;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Adds a child to the node. 
        /// </summary>
        protected void AddChild(NodeSource child)
        {
            if (m_children == null)
            {
                m_children = new List<NodeSource>();
            }

            m_children.Add(child);
        }

        /// <summary>
        /// Adds a child from the node. 
        /// </summary>
        protected void RemoveChild(NodeSource child)
        {
            if (m_children != null)
            {
                for (int ii = 0; ii < m_children.Count; ii++)
                {
                    if (Object.ReferenceEquals(m_children[ii], child))
                    {
                        m_children.RemoveAt(ii);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Reads an attribute for the node.
        /// </summary>
        protected virtual ServiceResult ReadAttribute(IOperationContext context, uint attributeId, DataValue value)
        {
            IList<string> locales = null;

            if (context != null)
            {
                locales = context.PreferredLocales;
            }

            switch (attributeId)
            {
                case Attributes.NodeId:
                {
                    value.Value = NodeId;
                    break;
                }

                case Attributes.NodeClass:
                {
                    value.Value = NodeClass;
                    break;
                }

                case Attributes.BrowseName:
                {
                    value.Value = BrowseName;
                    break;
                }

                case Attributes.DisplayName:
                {
                    value.Value = ResourceManager.Translate(locales, DisplayName);
                    break;
                }

                case Attributes.Description:
                {
                    value.Value = ResourceManager.Translate(locales, DisplayName);
                    break;
                }

                case Attributes.WriteMask:
                {
                    value.Value = (uint)WriteMask;
                    break;
                }

                case Attributes.UserWriteMask:
                {
                    value.Value = (uint)UserWriteMask;
                    break;
                }

                default:
                {
                    return StatusCodes.BadAttributeIdInvalid;
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Updates an attribute for the node.
        /// </summary>
        protected virtual ServiceResult WriteAttribute(uint attributeId, DataValue value)
        {
            // check for status/timestamp writes.
            if (value.StatusCode != StatusCodes.Good || value.ServerTimestamp != DateTime.MinValue || value.SourceTimestamp != DateTime.MinValue)
            {
                return StatusCodes.BadWriteNotSupported;
            }

            switch (attributeId)
            {
                case Attributes.BrowseName:
                {
                    m_browseName = (QualifiedName)value.Value;
                    break;
                }

                case Attributes.DisplayName:
                {
                    m_displayName = (LocalizedText)value.Value;
                    break;
                }

                case Attributes.Description:
                {
                    m_description = (LocalizedText)value.Value;
                    break;
                }

                case Attributes.WriteMask:
                {
                    m_writeMask = (AttributeWriteMask)(uint)value.Value;
                    break;
                }

                case Attributes.UserWriteMask:
                {
                    m_userWriteMask = (AttributeWriteMask)(uint)value.Value;
                    break;
                }

                default:
                {
                    return StatusCodes.BadAttributeIdInvalid;
                }
            }

            return ServiceResult.Good;
        }  
        
        /// <summary>
        /// Updates the attributes of the node.
        /// </summary>
        protected virtual void UpdateAttributes(ILocalNode source)
        {
            if (QualifiedName.IsNull(m_browseName))
            {
                m_browseName = source.BrowseName;
            }

            m_displayName   = source.DisplayName;
            m_description   = source.Description;
            m_writeMask     = source.WriteMask;
            m_userWriteMask = source.UserWriteMask;
        }  

        /// <summary>
        /// Copies the references from the source.
        /// </summary>
        protected virtual void UpdateReferences(ILocalNode source)
        {
            foreach (IReference reference in source.References)
            {
                // do not update type definition reference.
                if (!reference.IsInverse && reference.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                {
                    if (m_references.Find(reference.ReferenceTypeId, false, false, null).Count > 0)
                    {
                        continue;
                    }
                }

                m_references.Add(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
            }            
        }
           
        /// <summary>
        /// Initializes a node before it is created.
        /// </summary>
        protected void Initialize(
            NodeId        nodeId,
            QualifiedName browseName,
            uint          numericId)
        {
            m_nodeId     = nodeId;
            m_browseName = browseName;
            m_numericId  = numericId;
        }
        
        /// <summary>
        /// Creates the node in the server's address space.
        /// </summary>
        protected virtual void CreateNode(NodeId parentId, NodeId referenceTypeId)
        {
            // defined by the sub-class
        }
        
        /// <summary>
        /// Creates the children for the node.
        /// </summary>
        protected virtual BaseInstanceSource DeleteChild(BaseInstanceSource child)
        {  
            // delete from the address space.
            child.Delete();

            // dispose it.
            child.Dispose();

            // remove reference.
            return null;
        }

        /// <summary>
        /// Initializes the child nodes.
        /// </summary>
        protected virtual void InitializeChildren()
        {       
            // must be defined by the sub-class.
        }

        /// <summary>
        /// Creates the children for the node.
        /// </summary>
        protected virtual void CreateChildren(object configuration)
        {
            if (m_children != null)
            {
                foreach (BaseInstanceSource child in m_children)
                {
                    child.Create(this.NodeId, child.ReferenceTypeId, null, null, child.NumericId, configuration);
                }
            }
        }

        /// <summary>
        /// Deletes the children for the node.
        /// </summary>
        protected virtual void DeleteChildren()
        {
            if (m_children != null)
            {
                foreach (NodeSource child in m_children)
                {
                    child.Delete();
                }
            }
        }
        
        /// <summary>
        /// Called before the node is initialized from a source
        /// </summary>
        protected virtual void OnBeforeInitialize()
        {
            // must be defined by the sub-class.
        } 

        /// <summary>
        /// Called after the node has been initialized from a source.
        /// </summary>
        protected virtual void OnAfterInitialize()
        {
            // must be defined by the sub-class.
        } 

        /// <summary>
        /// Called before the node is created in the address space,
        /// </summary>
        protected virtual object OnBeforeCreate(object configuration)
        {
            return configuration;
        } 
        
        /// <summary>
        /// Applies the configuration to the node.
        /// </summary>
        protected virtual void ApplyConfiguration(object configuration)
        {
            NodeSet nodeset = configuration as NodeSet;

            if (nodeset != null)
            {               
                // check for a serialized node.
                Node node = nodeset.Find(this.NodeId, NamespaceUris);

                if (node != null)
                {
                    // translate any namespace indexes in the serialized node.
                    Node importedNode = nodeset.Copy(node, NamespaceUris, ServerUris);

                    // update the attributes.
                    UpdateAttributes(importedNode);

                    // translate and update references.
                    foreach (IReference reference in importedNode.References)
                    {                                 
                        m_references.Add(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                    }            
                }
            }
        } 
        
        /// <summary>
        /// Called when the node has been created in address space.
        /// </summary>
        protected virtual void OnAfterCreate(object configuration)
        {
            // must be defined by the sub-class.
        } 
        
        /// <summary>
        /// Called before the object deletes its nodes from the address space.
        /// </summary>
        protected virtual void OnBeforeDelete()
        {
            // must be defined by the sub-class.
        }      

        /// <summary>
        /// Called after the object is deletes its nodes.
        /// </summary>
        protected virtual void OnAfterDelete()
        {
            // must be defined by the sub-class.
        }  
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private bool m_disposed;
        private bool m_created;
        private IServerInternal m_server;
        private NodeSource m_parent;
        private NodeId m_nodeId;
        private object m_handle;
        private uint m_numericId;
        private QualifiedName m_browseName;
        private LocalizedText m_displayName;
        private LocalizedText m_description;
        private AttributeWriteMask m_writeMask;
        private AttributeWriteMask m_userWriteMask;
        private ReferenceCollection m_references;
        private List<NodeSource> m_children;
        private Dictionary<uint,IMonitoredItem> m_monitoredItems;
        #endregion
    }
    
    /// <summary> 
    /// The base class for all type nodes.
    /// </summary>
    [Obsolete("The BaseTypeSource class is obsolete and is not supported. See Opc.Ua.BaseTypeState for a replacement.")]
    public abstract class BaseTypeSource : NodeSource
    {
        #region Constructors
        /// <summary>
        /// Adds the source to the type table.
        /// </summary>
        protected BaseTypeSource(IServerInternal server)
        : 
            base(server, null)
        {            
        }

        /// <summary>
        /// Finds the source for the type definition (creates it if it does not exist).
        /// </summary>
        public static BaseTypeSource FindSource(IServerInternal server)
        {
            throw new NotImplementedException("Must be overridden by subclass.");
        }
        #endregion

        #region Public Members
        /// <summary cref="NodeSource.Initialize(NodeSource)" />
        public override void Initialize(NodeSource source)
        {
            lock (DataLock)
            {
                base.Initialize(source);
                
                BaseTypeSource type = (BaseTypeSource)source;

                m_baseTypeId = type.m_baseTypeId;
            }
        }

        /// <summary>
        /// The identifier for the base type node.
        /// </summary>
        public NodeId BaseTypeId
        {
            get
            {
                return m_baseTypeId;
            }
        }

        /// <summary>
        /// Creates the object type.
        /// </summary>
        public void Create(object configuration)
        {
            base.Create(m_baseTypeId, ReferenceTypeIds.HasSubtype, null, null, 0, configuration);
        }
        #endregion    
        
        #region Protected Members
        /// <summary>
        /// Initializes the type before creating it.
        /// </summary>
        protected override object OnBeforeCreate(object configuration)
        {
            // check the base type.
            ILocalNode baseType = NodeManager.GetLocalNode(m_baseTypeId);

            if (baseType == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown, 
                    "The specified base type does not exist: {0}",
                    m_baseTypeId);
            }

            // check the node class.
            if (baseType.NodeClass != NodeClass)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdRejected, 
                    "The specified base type is not a {1}: {0}",
                    baseType,
                    NodeClass);
            }

            // set the attributes to the default from the type.
            UpdateAttributes(baseType);
            
            return base.OnBeforeCreate(configuration);
        }

        /// <summary>
        /// Sets the NodeId, BrowseName and BaseType
        /// </summary>
        protected virtual NodeId Initialize(
            NodeId          nodeId,
            QualifiedName   browseName,
            NodeId          baseTypeId)
        { 
            lock (DataLock)
            {
                // do any pre-initialize processing.
                OnBeforeInitialize();

                // initialize the base.
                base.Initialize(nodeId, browseName, 0);

                // select a suitable default base type.
                if (NodeId.IsNull(baseTypeId))
                {
                    switch (NodeClass)
                    {
                        case NodeClass.ObjectType:    { baseTypeId = ObjectTypes.BaseObjectType; break; }
                        case NodeClass.VariableType:  { baseTypeId = VariableTypes.BaseDataVariableType; break; }
                        case NodeClass.DataType:      { baseTypeId = DataTypes.BaseDataType; break; }
                        case NodeClass.ReferenceType: { baseTypeId = ReferenceTypes.References; break; }
                    }
                }

                m_baseTypeId = baseTypeId;
                
                // initialize the children.
                InitializeChildren();
                
                // do any post-initialize processing.
                OnAfterInitialize();

                return NodeId;
            }
        }
        #endregion

        #region Private Fields
        private NodeId m_baseTypeId;
        #endregion
    }
    
    /// <summary> 
    /// The base class for all instance nodes.
    /// </summary>
    [Obsolete("The BaseInstanceSource class is obsolete and is not supported. See Opc.Ua.BaseInstanceState for a replacement.")]
    public abstract class BaseInstanceSource : NodeSource, INode
    {
        #region Constructors
        /// <summary>
        /// Adds the source to the type table.
        /// </summary>
        protected BaseInstanceSource(
            IServerInternal server,
            NodeSource      parent)
        : 
            base(server, parent)
        {
        }
        #endregion
                
        #region INode Members
        /// <summary cref="INode.TypeDefinitionId" />
        ExpandedNodeId INode.TypeDefinitionId
        {
            get
            {
                return m_typeDefinitionId;
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Updates the object based from a configuration.
        /// </summary>
        public override void Create(
            NodeId          parentId,
            NodeId          referenceTypeId, 
            NodeId          nodeId,
            QualifiedName   browseName, 
            uint            numericId,
            object          configuration)
        {          
            CheckNodeManagerState();

            lock (DataLock)
            {
                // update the reference type.
                if (NodeId.IsNull(referenceTypeId))
                {
                    referenceTypeId = m_referenceTypeId;
                }
                else
                {                        
                    m_referenceTypeId = referenceTypeId;
                }

                // create the node.
                base.Create(parentId, referenceTypeId, nodeId, browseName, numericId, configuration);
            }
        }

        /// <summary cref="NodeSource.Initialize(NodeSource)" />
        public override void Initialize(NodeSource source)
        {
            lock (DataLock)
            {
                base.Initialize(source);
                
                BaseInstanceSource instance = (BaseInstanceSource)source;

                m_referenceTypeId = instance.m_referenceTypeId;
                m_typeDefinitionId = instance.m_typeDefinitionId;
            }
        }

        /// <summary>
        /// Sets the NodeId, BrowseName and TypeDefinition
        /// </summary>
        public virtual NodeId Initialize(
            NodeId          referenceTypeId,
            NodeId          nodeId,
            QualifiedName   browseName,
            uint            numericId,
            NodeId          typeDefinitionId)
        { 
            lock (DataLock)
            {
                // do any pre-initialize processing.
                OnBeforeInitialize();

                // initialize the base.
                base.Initialize(nodeId, browseName, numericId);

                // save the reference type to the parent.
                m_referenceTypeId = referenceTypeId;
                    
                // select a suitable type definition.
                if (NodeClass == NodeClass.Object)
                {
                    if (NodeId.IsNull(typeDefinitionId))
                    {
                        typeDefinitionId = ObjectTypes.BaseObjectType;
                    }
                }
                else if (NodeClass == NodeClass.Variable)
                {
                    if (NodeId.IsNull(typeDefinitionId))
                    {
                        typeDefinitionId = VariableTypes.BaseDataVariableType;
                    }
                }
                
                m_typeDefinitionId = typeDefinitionId;

                // methods don't have a type definition.
                if (!NodeId.IsNull(typeDefinitionId))
                {
                    References.RemoveAll(ReferenceTypeIds.HasTypeDefinition, false);
                    References.Add(ReferenceTypeIds.HasTypeDefinition, false, m_typeDefinitionId);
                }
                
                // initialize the children.
                InitializeChildren();

                // do any post-initialize processing.
                OnAfterInitialize();

                return NodeId;
            }
        }
        
        /// <summary>
        /// The type of reference from the parent node to the instance.
        /// </summary>
        public NodeId ReferenceTypeId
        {
            get
            {
                return m_referenceTypeId;
            }
        }

        /// <summary>
        /// The identifier for the type definition node.
        /// </summary>
        public new NodeId TypeDefinitionId
        {
            get
            {
                return m_typeDefinitionId;
            }
        }

        /// <summary>
        /// Reads the value for an attribute of a node.
        /// </summary>
        public DataValue ReadAttributeValue(
            IOperationContext    context, 
            ExpandedNodeId       typeDefinitionId,
            IList<QualifiedName> relativePath,
            uint                 attributeId,
            NumericRange         indexRange)
        {           
            lock (DataLock)
            {         
                // check for match on the event type being searched. 
                if (!Server.TypeTree.IsTypeOf(TypeDefinitionId, typeDefinitionId))
                {
                    return new DataValue(StatusCodes.BadNotSupported);
                }

                // find the target.
                NodeSource target = this;

                if (relativePath != null && relativePath.Count > 0)
                {                     
                    target = Find(relativePath, 0);

                    if (target == null)
                    {
                        return new DataValue(StatusCodes.BadNotSupported);
                    }
                }
                    
                // read the attribute value.
                DataValue dataValue = new DataValue();
                
                ServiceResult result = target.Read(context, attributeId, dataValue);

                if (ServiceResult.IsBad(result))
                {
                    return new DataValue(result.StatusCode);
                }

                // apply any index range.
                object value = dataValue.Value;

                result = indexRange.ApplyRange(ref value);

                if (ServiceResult.IsBad(result))
                {
                    return new DataValue(result.StatusCode);
                }

                dataValue.Value = value;

                // return the value.
                return dataValue;                
            }
        }

        /// <summary>
        /// Recursively sets the minimum sampling interval for all variables.
        /// </summary>
        public void SetMinimumSamplingInterval(double samplingInterval)
        {
            lock (DataLock)
            {
                VariableSource variable = this as VariableSource;

                if (variable != null)
                {
                    variable.MinimumSamplingInterval = samplingInterval;
                }

                if (this.Children != null)
                { 
                    foreach (NodeSource child in this.Children)
                    {
                        BaseInstanceSource instance = child as BaseInstanceSource;

                        if (instance != null)
                        {
                            instance.SetMinimumSamplingInterval(samplingInterval);
                        }
                    }
                }
            }
        }
        #endregion 

        #region Protected Members
        /// <summary>
        /// Initializes the instance before creating it.
        /// </summary>
        protected override object OnBeforeCreate(object configuration)
        {
            // determine the node class for the type definition.
            NodeClass typeClass = NodeClass.Method;
                        
            if (NodeClass == NodeClass.Object)
            {
                typeClass = NodeClass.ObjectType;
            }
            else if (NodeClass == NodeClass.Variable)
            {
                typeClass = NodeClass.VariableType;
            }

            // read and validate the type definition.
            if (typeClass != NodeClass.Method)
            {
                ILocalNode typeDefinition = NodeManager.GetLocalNode(m_typeDefinitionId);

                if (typeDefinition == null)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown, 
                        "The specified type definition does not exist: {0}",
                        m_typeDefinitionId);
                }
                
                // check the node class.
                if (typeDefinition.NodeClass != typeClass)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdRejected, 
                        "The specified type definition is not a {1}: {0}",
                        typeDefinition,
                        typeClass);
                }
            }

            return base.OnBeforeCreate(configuration);
        }

        /// <summary>
        /// Adds references to a shared child.
        /// </summary>
        protected virtual BaseInstanceSource ReferenceSharedChild(BaseInstanceSource child)
        {  
            CheckNodeManagerState();

            if (child != null)
            {
                NodeManager.ReferenceSharedNode(this, child.ReferenceTypeId, false, child.BrowseName);
            }

            return child;
        }        
        
        /// <summary>
        /// Removes references to a shared child.
        /// </summary>
        protected virtual BaseInstanceSource UnreferenceSharedChild(BaseInstanceSource child)
        {  
            CheckNodeManagerState();

            if (child != null)
            {
                NodeManager.UnreferenceSharedNode(this, child.ReferenceTypeId, false, child.BrowseName);
            }

            return null;
        }        

        /// <summary>
        /// Replaces a shared child in the address space.
        /// </summary>
        protected virtual BaseInstanceSource ReplaceSharedChild(
            BaseInstanceSource child, 
            NodeId             nodeId,
            NodeId             referenceTypeId,
            QualifiedName      browseName,
            uint               numericId,
            NodeId             typeDefinitionId,
            object             configuration)
        {  
            CheckNodeManagerState();

            // remove links to shared node.
            NodeManager.UnreferenceSharedNode(this, referenceTypeId, false, browseName);

            // initialize replacement.
            child.Initialize(nodeId, referenceTypeId, browseName, numericId, typeDefinitionId);

            // add replacement to the address space.
            child.Create(this.NodeId, child.ReferenceTypeId, null, null, child.NumericId, configuration);
            
            // return replacement.
            return child;
        }        

        /// <summary>
        /// Replaces a shared child in the address space.
        /// </summary>
        protected virtual BaseInstanceSource DeleteReplacedChild(BaseInstanceSource child)
        {  
            CheckNodeManagerState();

            // save reference and browse name info.
            NodeId referenceTypeId = child.ReferenceTypeId;
            QualifiedName browseName = child.BrowseName;

            // delete from the address space.
            child.Delete();

            // dispose it.
            child.Dispose();

            // add reference to shared child.
            NodeManager.ReferenceSharedNode(this, referenceTypeId, false, browseName);

            // remove reference.
            return null;
        }
        
        /// <summary>
        /// Initializes an optinal child based on what is in the address space.
        /// </summary>
        protected BaseInstanceSource InitializeOptionalChild(
            ConstructInstanceDelegate constructInstanceDelegate,
            NodeId                    referenceTypeId,
            QualifiedName             browseName,
            uint                      numericId,
            object                    configuration)
        {
            CheckNodeManagerState();

            // check if nothing to do.
            if (constructInstanceDelegate == null)
            {
                return null;
            }

            // check if node exists.
            ILocalNode existingChild = NodeManager.GetTargetNode(
                this.NodeId, 
                referenceTypeId, 
                false, 
                true, 
                browseName) as ILocalNode;

            if (existingChild == null)
            {
                return null;
            }

            BaseInstanceSource child = constructInstanceDelegate(
                Server, 
                this, 
                referenceTypeId, 
                null, 
                browseName,
                numericId);
                                            
            // create it.
            child.Create(this.NodeId, child.ReferenceTypeId, null, null, numericId, configuration);

            return child;
        }

        /// <summary>
        /// Initializes an shared child based on what is in the address space.
        /// </summary>
        protected BaseInstanceSource InitializeSharedChild(
            BaseInstanceSource        sharedChild,
            ConstructInstanceDelegate constructInstanceDelegate,
            NodeId                    referenceTypeId,
            QualifiedName             browseName,
            uint                      numericId,
            object                    configuration)
        {
            CheckNodeManagerState();

            // check if node exists.
            ILocalNode existingChild = NodeManager.GetTargetNode(
                this.NodeId, 
                referenceTypeId, 
                false, 
                true, 
                browseName) as ILocalNode;

            if (existingChild == null)
            {
                if (sharedChild != null && sharedChild.Parent != this)
                {
                    ReferenceSharedChild(sharedChild);
                }

                return null;
            }

            // check if existing child is owned by the object.
            ExpandedNodeId parentId = existingChild.References.FindTarget(ReferenceTypeIds.HasModelParent, false, false, null, 0);

            if (parentId != this.NodeId)
            {
                return sharedChild;
            }
                                
            // construct the child.
            BaseInstanceSource child = constructInstanceDelegate(
                Server, 
                this, 
                referenceTypeId, 
                null, 
                browseName,
                numericId);
            
            // create it.
            child.Create(this.NodeId, child.ReferenceTypeId, null, null, numericId, configuration);

            return child;
        }
        #endregion

        #region Private Fields
        private NodeId m_referenceTypeId;
        private NodeId m_typeDefinitionId;
        #endregion
    }
   
    /// <summary>
    /// Stores the sources for types with shared components defined.
    /// </summary>
    [Obsolete("The TypeSourceTable class is obsolete and is not supported.")]
    public class TypeSourceTable
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        public TypeSourceTable()
        {
            m_sources = new Dictionary<NodeId,BaseTypeSource>();
        }
        #endregion
        
        #region Public Interface
        /// <summary>
        /// A object used to synchronize access to the table.
        /// </summary>
        public object SyncRoot
        {
            get { return m_lock; } 
        }

        /// <summary>
        /// Returns the source for a types that has shared components defined.
        /// </summary>
        /// <remarks>
        /// Some types define shared components which are used by all instances of the type. This
        /// table contains sources for those shared components.
        /// </remarks>
        public BaseTypeSource FindTypeSource(NodeId typeId)
        {
            if (NodeId.IsNull(typeId))
            {
                return null;
            }
            
            lock (m_lock)
            {
                BaseTypeSource source = null;

                if (m_sources.TryGetValue(typeId, out source))
                {
                    return source;
                }

                return null;
            }
        }
                
        /// <summary>
        /// Sets the source for a types that has shared components defined.
        /// </summary>
        /// <remarks>
        /// Removes the entry from the table if the source is null.
        /// </remarks>
        public void SetTypeSource(NodeId typeId, BaseTypeSource source)
        {
            if (NodeId.IsNull(typeId))
            {
                throw new ArgumentNullException("typeId");
            }
            
            lock (m_lock)
            {
                if (source != null)
                {
                    m_sources[typeId] = source;
                }
                else
                {
                    m_sources.Remove(typeId);
                }
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private Dictionary<NodeId,BaseTypeSource> m_sources;
        #endregion
    }

    /// <summary>
    /// A delegate for a function that constructs a new instance node.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public delegate BaseInstanceSource ConstructInstanceDelegate(
        IServerInternal server, 
        NodeSource      parent, 
        NodeId          referenceTypeId,
        NodeId          nodeId,
        QualifiedName   browseName,
        uint            numericId);
#endif
}
