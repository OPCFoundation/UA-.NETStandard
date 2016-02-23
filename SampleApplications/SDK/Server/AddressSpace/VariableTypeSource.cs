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
    /// The base class for custom VariableType nodes.
    /// </summary>
    [Obsolete("The VariableTypeSource<T> class is obsolete and is not supported. See Opc.Ua.BaseVariableTypeState for a replacement.")]
    public class VariableTypeSource<T> : BaseTypeSource, IVariableType
    {
        #region Constructors
        /// <summary>
        /// Adds the source to the type table.
        /// </summary>
        public VariableTypeSource(IServerInternal server) : base(server)
        {
        }
        #endregion
        
        #region ICloneable Members
        /// <summary cref="NodeSource.Clone(NodeSource)" />
        public override NodeSource Clone(NodeSource parent)
        {
            lock (DataLock)
            {
                VariableTypeSource<T> clone = new VariableTypeSource<T>(Server);
                clone.Initialize(this);
                return clone;      
            }
        }
        #endregion

		#region IDisposable Members
        /// <summary>
        /// Frees resources held by the object.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (Disposed)
            {
                if (disposing)
                {
                    if (typeof(IDisposable).IsInstanceOfType(m_value))
                    {
                        ((IDisposable)m_value).Dispose();
                    }
                }

                m_value = default(T);
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The default value for instances of the VariableType.
        /// </summary>
        public T Value
        {
            get
            {
                lock (DataLock)
                {
                    return m_value;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_value = (T)Utils.Clone(value);
                }
            }
        }
        #endregion
        
        #region INode Members
        /// <summary cref="INode.NodeClass" />
        public override NodeClass NodeClass
        {
            get
            {
                return NodeClass.VariableType;
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
                    case Attributes.Value:
                    case Attributes.DataType:
                    case Attributes.ValueRank:
                    case Attributes.ArrayDimensions:                        
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

        #region IVariableBase Members
        /// <summary cref="IVariableBase.Value" />
        object IVariableBase.Value
        {
            get 
            { 
                return this.Value;  
            }
            
            set 
            { 
                if (!typeof(T).IsInstanceOfType(value))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch,
                        "VariableType value must be a instance of a(n) {0}.",
                        typeof(T).Name);                        
                }

                this.Value = (T)value; 
            }
        }

        /// <summary cref="IVariableBase.DataType" />
        public NodeId DataType
        {
            get
            {
                lock (DataLock)
                {
                    return m_datatype;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_datatype = value;
                }
            }
        }

        /// <summary cref="IVariableBase.ValueRank" />
        public int ValueRank
        {
            get
            {
                lock (DataLock)
                {
                    return m_valueRank;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_valueRank = value;
                }
            }
        }

        /// <summary cref="IVariableBase.ArrayDimensions" />
        public IList<uint> ArrayDimensions
        {
            get
            {
                lock (DataLock)
                {
                    return m_arrayDimensions;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_arrayDimensions = value;
                }
            }
        }
        #endregion

        #region IVariableType Members
        /// <summary cref="IVariableType.IsAbstract" />
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
        
        #region Overridden Method
        /// <summary cref="NodeSource.Initialize(NodeSource)" />
        public override void Initialize(NodeSource source)
        {
            lock (DataLock)
            {
                base.Initialize(source);
                
                VariableTypeSource<T> type = (VariableTypeSource<T>)source;

                m_value = (T)Utils.Clone(type.m_value);
                m_datatype = type.m_datatype;
                m_valueRank = type.m_valueRank;
                m_arrayDimensions = (IList<uint>)Utils.Clone(type.m_arrayDimensions);
                m_isAbstract = type.m_isAbstract;
            }
        }

        /// <summary cref="NodeSource.CreateNode" />
        protected override void CreateNode(NodeId parentId, NodeId referenceTypeId)
        {
            CheckNodeManagerState();

            VariableTypeAttributes attributes = new VariableTypeAttributes();

            attributes.SpecifiedAttributes = (uint)NodeAttributesMask.DisplayName;
            attributes.DisplayName         = DisplayName;

            NodeManager.CreateVariableType(
                parentId,
                NodeId,
                BrowseName,
                attributes);
        }

        /// <summary cref="NodeSource.UpdateAttributes" />
        protected override void UpdateAttributes(ILocalNode source)
        {
            base.UpdateAttributes(source);

            IVariableType type = source as IVariableType;

            if (type != null)
            {
                if (typeof(T).IsInstanceOfType(type.Value))
                {
                    Value = (T)type.Value;
                }

                DataType        = type.DataType;
                ValueRank       = type.ValueRank;
                ArrayDimensions = null;
                IsAbstract      = type.IsAbstract;

                if (type.ArrayDimensions != null && type.ArrayDimensions.Count > 0)
                {
                    ArrayDimensions = (IList<uint>)Utils.Clone(type.ArrayDimensions);
                }
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

                case Attributes.Value:
                {
                    value.Value = Value;
                    break;
                }

                case Attributes.DataType:
                {
                    value.Value = DataType;
                    break;
                }

                case Attributes.ValueRank:
                {
                    value.Value = ValueRank;
                    break;
                }

                case Attributes.ArrayDimensions:
                {
                    value.Value = ArrayDimensions;
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

                case Attributes.Value:
                {
                    Value = (T)value.Value;
                    break;
                }

                case Attributes.DataType:
                {
                    DataType = (NodeId)value.Value;
                    break;
                }

                case Attributes.ValueRank:
                {
                    ValueRank = (int)value.Value;
                    break;
                }

                case Attributes.ArrayDimensions:
                {
                    ArrayDimensions = (IList<uint>)value.Value;
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
        private T m_value;
        private NodeId m_datatype;
        private int m_valueRank;
        private IList<uint> m_arrayDimensions;
        private bool m_isAbstract;
        #endregion
    }
#endif
}
