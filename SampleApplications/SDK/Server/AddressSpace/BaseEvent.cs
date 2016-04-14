/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.Runtime.Serialization;

#pragma warning disable 0618

namespace Opc.Ua.Server
{
#if LEGACY_CORENODEMANAGER
    [Obsolete("The BaseEvent class is obsolete and is not supported. See Opc.Ua.BaseEventState for a replacement.")]
	public partial class BaseEvent : IFilterTarget
	{        
	    #region IFilterTarget Members
        /// <summary cref="IFilterTarget.IsTypeOf" />
        public bool IsTypeOf(
            FilterContext context, 
            NodeId        typeDefinitionId)
        {
            if (context == null) throw new ArgumentNullException("context");
            
            return context.TypeTree.IsTypeOf(TypeDefinitionId, typeDefinitionId);
        }

        /// <summary cref="IFilterTarget.IsInView" />
        public bool IsInView(
            FilterContext context, 
            NodeId        viewId)
        {
            // events instances are not in any view.
            return false;
        }
                
        /// <summary cref="IFilterTarget.IsRelatedTo" />
        public virtual bool IsRelatedTo(
            FilterContext context, 
            NodeId        source,
            NodeId        targetTypeId,
            NodeId        referenceTypeId,
            int           hops)
        {
            // events instances do not have any relationships to other nodes.
            return false;
        }

        /// <summary cref="IFilterTarget.GetAttributeValue" />
        public virtual object GetAttributeValue(
            FilterContext        context,
            NodeId               typeDefinitionId,
            IList<QualifiedName> relativePath,
            uint                 attributeId,
            NumericRange         indexRange)
        {
            if (context == null) throw new ArgumentNullException("context");
            
            // read the attribute value.
            DataValue dataValue = ReadAttributeValue(
                context,
                typeDefinitionId,
                relativePath,
                attributeId,
                indexRange);

            if (StatusCode.IsBad(dataValue.StatusCode))
            {
                return dataValue.StatusCode;
            }
                        
            // return the value.
            return dataValue.Value;
        }
	    #endregion
        
	    #region Public Methods
        /// <summary>
        /// Sets the EventId, EventType, Time, ReceiveTime, TimeZone and DaylightSavingsTime properties.
        /// </summary>
        public void InitializeNewEvent()
        {
            EventId.Value     = Guid.NewGuid().ToByteArray();
            EventType.Value   = ExpandedNodeId.ToNodeId(this.TypeDefinitionId, Server.NamespaceUris);
            Time.Value        = DateTime.UtcNow;
            ReceiveTime.Value = DateTime.UtcNow;
        }
	    #endregion
    }
       
    #region GenericEvent Class
    /// <summary>
    /// Represents an instance of the BaseEventType ObjectType in the address space.
    /// </summary>
    [Obsolete("The GenericEvent class is obsolete and is not supported. See Opc.Ua.InstanceStateSnapshot for a replacement.")]
    public partial class GenericEvent : BaseEvent
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        protected GenericEvent(IServerInternal server, NodeSource parent) 
        : 
            base(server, parent)
        {
            m_values = new Dictionary<QualifiedName,object>();
        }

        /// <summary>
        /// Creates a new instance of the node without any parent.
        /// </summary>
        public static GenericEvent Construct(IServerInternal server, NodeId typeDefinitionId)
        {
            GenericEvent instance = new GenericEvent(server, (NodeSource)null);
            instance.Initialize(null, null, null, 0, typeDefinitionId);
            return instance;
        }
        #endregion
           
        #region ICloneable Members
        /// <summary cref="NodeSource.Clone(NodeSource)" />
        public override NodeSource Clone(NodeSource parent)
        {
            lock (DataLock)
            {
                GenericEvent clone = new GenericEvent(Server, parent);
                clone.Initialize(this);
                return clone;
            } 
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Sets the value of a property for an event. 
        /// </summary>
        public void SetProperty(QualifiedName browseName, object value)
        {
            m_values[browseName] = value;
        }
        #endregion

        #region Overridden Methods
        /// <summary cref="IFilterTarget.GetAttributeValue" />
        public override object GetAttributeValue(
            FilterContext        context,
            NodeId               typeDefinitionId,
            IList<QualifiedName> relativePath,
            uint                 attributeId,
            NumericRange         indexRange)
        {
            if (context == null) throw new ArgumentNullException("context");

            // check type definition.
            if (!Server.TypeTree.IsTypeOf(this.TypeDefinitionId, typeDefinitionId))
            {
                return null;
            }

            // lookup extended properties.
            if (relativePath == null || relativePath.Count == 1)
            {
                object value = null;

                if (m_values.TryGetValue(relativePath[0], out value))
                {
                    return value;
                }
            }

            // read the attribute value.
            DataValue dataValue = ReadAttributeValue(
                context,
                typeDefinitionId,
                relativePath,
                attributeId,
                indexRange);

            if (StatusCode.IsBad(dataValue.StatusCode))
            {
                return dataValue.StatusCode;
            }
                        
            // return the value.
            return dataValue.Value;
        }
        #endregion

        #region Private Fields
        private Dictionary<QualifiedName,object> m_values;
        #endregion
    }
    #endregion
#endif
}
