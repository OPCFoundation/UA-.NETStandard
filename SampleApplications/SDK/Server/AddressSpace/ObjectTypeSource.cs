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
