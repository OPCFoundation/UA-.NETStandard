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
    /// The base class for custom ObjectType nodes.
    /// </summary>
    [Obsolete("The ObjectTypeSource class is obsolete and is not supported. See Opc.Ua.BaseObjectTypeState for a replacement.")]
    public class ObjectTypeSource : BaseTypeSource, IObjectType
    {
        #region Constructors
        /// <summary>
        /// Adds the source to the type table.
        /// </summary>
        public ObjectTypeSource(IServerInternal server) : base(server)
        {
        }
        #endregion
        
        #region ICloneable Members
        /// <summary cref="NodeSource.Clone(NodeSource)" />
        public override NodeSource Clone(NodeSource parent)
        {
            lock (DataLock)
            {
                ObjectTypeSource clone = new ObjectTypeSource(Server);
                clone.Initialize(this);
                return clone;
            }
        }
        
        /// <summary cref="NodeSource.Initialize(NodeSource)" />
        public override void Initialize(NodeSource source)
        {
            lock (DataLock)
            {
                base.Initialize(source);

                ObjectTypeSource type = (ObjectTypeSource)source;

                m_isAbstract = type.m_isAbstract;
            }
        }
        #endregion

        #region INode Members
        /// <summary cref="INode.NodeClass" />
        public override NodeClass NodeClass
        {
            get
            {
                return NodeClass.ObjectType;
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
                    case Attributes.IsAbstract:
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

        #region IObjectType Members
        /// <summary cref="IObjectType.IsAbstract" />
        public bool IsAbstract
        {
            get
            {
                lock (DataLock)
                {
                    return m_isAbstract;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_isAbstract = value;
                }
            }
        }
        #endregion
        
        #region Overridden Methods
        /// <summary cref="NodeSource.CreateNode" />
        protected override void CreateNode(NodeId parentId, NodeId referenceTypeId)
        {
            CheckNodeManagerState();

            ObjectTypeAttributes attributes = new ObjectTypeAttributes();

            attributes.SpecifiedAttributes = (uint)NodeAttributesMask.DisplayName;
            attributes.DisplayName         = DisplayName;

            NodeManager.CreateObjectType(
                parentId,
                NodeId,
                BrowseName,
                attributes);
        }

        /// <summary cref="NodeSource.UpdateAttributes" />
        protected override void UpdateAttributes(ILocalNode source)
        {
            base.UpdateAttributes(source);

            IObjectType type = source as IObjectType;

            if (type != null)
            {
                m_isAbstract = type.IsAbstract;
            }
        }

        /// <summary cref="NodeSource.ReadAttribute" />
        protected override ServiceResult ReadAttribute(IOperationContext context, uint attributeId, DataValue value)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:
                {
                    value.Value = IsAbstract;
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
                case Attributes.IsAbstract:
                {
                    IsAbstract = (bool)value.Value;
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

        #region Private Fields
        private bool m_isAbstract;
        #endregion
    }
#endif
}
