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
    /// The base class for custom Object nodes.
    /// </summary>
    [Obsolete("The ObjectSource class is obsolete and is not supported. See Opc.Ua.BaseObjectState for a replacement.")]
    public class ObjectSource : BaseInstanceSource, IObject, IEventSource
    {
        #region Constructors
        /// <summary>
        /// Adds the source to the type table.
        /// </summary>
        public ObjectSource(
            IServerInternal server,
            NodeSource      parent)
        : 
            base(server, parent)
        {
        }

        /// <summary>
        /// Creates a new instance of the node.
        /// </summary>
        public static ObjectSource Construct(
            IServerInternal server, 
            NodeSource      parent, 
            NodeId          referenceTypeId,
            NodeId          nodeId,
            QualifiedName   browseName,
            uint            numericId)
        {
            ObjectSource instance = new ObjectSource(server, parent);
            instance.Initialize(referenceTypeId, nodeId, browseName, numericId, ObjectTypes.BaseObjectType);
            return instance;
        }

        /// <summary>
        /// Creates a new instance of the node.
        /// </summary>
        public static ObjectSource Construct(IServerInternal server)
        {
            ObjectSource instance = new ObjectSource(server, null);
            instance.Initialize(null, null, null, 0, ObjectTypes.BaseObjectType);
            return instance;
        }
        #endregion
        
        #region ICloneable Members
        /// <summary cref="NodeSource.Clone(NodeSource)" />
        public override NodeSource Clone(NodeSource parent)
        {
            lock (DataLock)
            {
                ObjectSource clone = this;
                clone.Initialize(this);
                return clone;
            }
        }
        #endregion

        #region INode Members
        /// <summary cref="INode.NodeClass" />
        public override NodeClass NodeClass
        {
            get
            {
                return NodeClass.Object;
            }
        }
        #endregion

        #region ILocalNode Members
        /// <summary cref="ILocalNode.SupportsAttribute" />
        public override bool SupportsAttribute(uint attributeId)
        {
            lock (DataLock)
            {
                switch (attributeId)
                {
                    case Attributes.EventNotifier:
                    {
                        return true;
                    }

                    default:
                    {
                        return base.SupportsAttribute(attributeId);
                    }
                }
            }
        }
        #endregion

        #region IObject Members
        /// <summary cref="IObjectType.IsAbstract" />
        public byte EventNotifier
        {
            get
            {
                lock (DataLock)
                {
                    return m_eventNotifier;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_eventNotifier = value;
                }
            }
        }
        #endregion
        
        #region Overridden Methods
        /// <summary cref="NodeSource.Initialize(NodeSource)" />
        public override void Initialize(NodeSource source)
        {
            lock (DataLock)
            {
                base.Initialize(source);
                
                ObjectSource instance = (ObjectSource)source;

                m_eventNotifier = instance.m_eventNotifier;
            }
        }

        /// <summary cref="NodeSource.CreateNode" />
        protected override void CreateNode(NodeId parentId, NodeId referenceTypeId)
        {
            CheckNodeManagerState();

            ObjectAttributes attributes = new ObjectAttributes();

            attributes.SpecifiedAttributes = (uint)NodeAttributesMask.DisplayName;
            attributes.DisplayName         = DisplayName;

            NodeManager.CreateObject(
                parentId,
                referenceTypeId,
                NodeId,
                BrowseName,
                attributes,
                TypeDefinitionId);
        }

        /// <summary cref="NodeSource.UpdateAttributes" />
        protected override void UpdateAttributes(ILocalNode source)
        {
            base.UpdateAttributes(source);

            IObject instance = source as IObject;

            if (instance != null)
            {
                m_eventNotifier = instance.EventNotifier;
            }
        }

        /// <summary cref="NodeSource.ReadAttribute" />
        protected override ServiceResult ReadAttribute(IOperationContext context, uint attributeId, DataValue value)
        {
            switch (attributeId)
            {
                case Attributes.EventNotifier:
                {
                    value.Value = EventNotifier;
                    break;
                }

                default:
                {
                    return base.ReadAttribute(context, attributeId, value);
                }
            }

            return ServiceResult.Good;
        }

        /// <summary cref="NodeSource.WriteAttribute" />
        protected override ServiceResult WriteAttribute(uint attributeId, DataValue value)
        {
            // check for status/timestamp writes.
            if (value.StatusCode != StatusCodes.Good || value.ServerTimestamp != DateTime.MinValue || value.SourceTimestamp != DateTime.MinValue)
            {
                return StatusCodes.BadWriteNotSupported;
            }

            switch (attributeId)
            {
                case Attributes.EventNotifier:
                {
                    EventNotifier = (byte)value.Value;
                    break;
                }

                default:
                {
                    return  base.WriteAttribute(attributeId, value);
                }
            }

            return ServiceResult.Good;
        }  
        #endregion
                
        #region IEventSource Members
        /// <summary cref="IEventSource.SubscribeToEvents" /> 
        public void SubscribeToEvents(
            OperationContext    context, 
            object              notifier, 
            uint                subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool                unsubscribe)
        {
            lock (DataLock)
            {
                if (unsubscribe)
                {
                    Unsubscribe(monitoredItem);
                }
                else
                {
                    Subscribe(monitoredItem);
                }
            }
        }
        
        /// <summary cref="IEventSource.SubscribeToAllEvents" /> 
        public void SubscribeToAllEvents(
            OperationContext    context, 
            uint                subscriptionId,
            IEventMonitoredItem monitoredItem,
            bool                unsubscribe)
        {
            if (context == null)  throw new ArgumentNullException("context");

            lock (DataLock)
            {
                if (unsubscribe)
                {
                    Unsubscribe(monitoredItem);
                }
                else
                {
                    Subscribe(monitoredItem);
                }
            }
        }

        /// <summary cref="IEventSource.ConditionRefresh" /> 
        public virtual void ConditionRefresh(            
            OperationContext           context,
            IList<IEventMonitoredItem> monitoredItems)
        {
            if (context == null)  throw new ArgumentNullException("context");

            lock (DataLock)
            {
                RefreshConditions(context, monitoredItems);
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Whether the object should report events to its parent.
        /// </summary>
        /// <remarks>
        /// This property is used for objects like conditions that are not part of the HasNotifier hierarchy.
        /// </remarks>
        public bool ReportEventsToParent
        {
            get
            {
                lock (DataLock)
                {
                    return m_reportEventsToParent;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_reportEventsToParent = value;
                }
            }
        }

        /// <summary>
        /// Reports an event produced by the object.
        /// </summary>
        public virtual void ReportEvent(BaseEvent e)
        {
            lock (DataLock)
            {
                if (m_reportEventsToParent)
                {
                    ObjectSource parent = Parent as ObjectSource;

                    if (parent != null)
                    {
                        parent.ReportEvent(e);
                    }
                }
                    
                List<IEventMonitoredItem> monitoredItems = new List<IEventMonitoredItem>();

                if (MonitoredItems != null)
                {
                    foreach (IMonitoredItem monitoredItem in MonitoredItems)
                    {
                        IEventMonitoredItem eventItem = monitoredItem as IEventMonitoredItem;

                        if (eventItem != null)
                        {
                            monitoredItems.Add(eventItem);
                        }
                    }

                    if (monitoredItems.Count > 0)
                    {
                        EventManager.ReportEvent(e, monitoredItems);
                    }
                }
            }
        }

        /// <summary>
        /// Returns refresh events for any conditions belonging to the object.
        /// </summary>
        protected virtual void RefreshConditions(
            OperationContext           context, 
            IList<IEventMonitoredItem> monitoredItems)
        {
            // not implemented.
        }
        #endregion

        #region Private Fields
        private byte m_eventNotifier;
        private bool m_reportEventsToParent;
        #endregion
    }

    #region Folder Class
    /// <summary>
    /// Represents an instance of the Folder ObjectType in the address space.
    /// </summary>
    [Obsolete("The Folder class is obsolete and is not supported. See Opc.Ua.FolderState for a replacement.")]
    public partial class Folder : ObjectSource
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        protected Folder(IServerInternal server, NodeSource parent) 
        : 
            base(server, parent)
        {
        }

        /// <summary>
        /// Creates a new instance of the node.
        /// </summary>
        public static new Folder Construct(
            IServerInternal server, 
            NodeSource      parent, 
            NodeId          referenceTypeId,
            NodeId          nodeId,
            QualifiedName   browseName,
            uint            numericId)
        {
            Folder instance = new Folder(server, parent);

            instance.Initialize(referenceTypeId, nodeId, browseName, numericId, ObjectTypes.FolderType);

            return instance;
        }
        #endregion     

        #region ICloneable Members
        /// <summary cref="NodeSource.Clone(NodeSource)" />
        public override NodeSource Clone(NodeSource parent)
        {
            lock (DataLock)
            {
                Folder clone = new Folder(Server, parent);
                clone.Initialize(this);
                return clone;
            }
        }
        #endregion             
    }
    #endregion
#endif
}
