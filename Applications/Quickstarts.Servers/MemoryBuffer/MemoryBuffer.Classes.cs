/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace MemoryBuffer
{
    #region MemoryTagState Class
    #if (!OPCUA_EXCLUDE_MemoryTagState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class MemoryTagState : BaseDataVariableState
    {
        #region Constructors
        /// <remarks />
        public MemoryTagState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(MemoryBuffer.VariableTypes.MemoryTagType, MemoryBuffer.Namespaces.MemoryBuffer, namespaceUris);
        }

        /// <remarks />
        protected override NodeId GetDefaultDataTypeId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.DataTypes.BaseDataType, Opc.Ua.Namespaces.OpcUa, namespaceUris);
        }

        /// <remarks />
        protected override int GetDefaultValueRank()
        {
            return ValueRanks.Scalar;
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACIAAABodHRwOi8vc2FtcGxlcy5vcmcvVUEvTWVtb3J5QnVmZmVy/////xVggQICAAAAAQAVAAAA" +
           "TWVtb3J5VGFnVHlwZUluc3RhbmNlAQH6AwEB+gP6AwAAABgBAf////8AAAAA";
        #endregion
        #endif
        #endregion

        #region Public Properties
        #endregion

        #region Overridden Methods
        #endregion

        #region Private Fields
        #endregion
    }

    #region MemoryTagState<T> Class
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public class MemoryTagState<T> : MemoryTagState
    {
        #region Constructors
        /// <remarks />
        public MemoryTagState(NodeState parent) : base(parent)
        {
            Value = default(T);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);

            Value = default(T);
            DataType = TypeInfo.GetDataTypeId(typeof(T));
            ValueRank = TypeInfo.GetValueRank(typeof(T));
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }
        #endregion

        #region Public Members
        /// <remarks />
        public new T Value
        {
            get
            {
                return CheckTypeBeforeCast<T>(((BaseVariableState)this).Value, true);
            }

            set
            {
                ((BaseVariableState)this).Value = value;
            }
        }
        #endregion
    }
    #endregion
    #endif
    #endregion

    #region MemoryBufferState Class
    #if (!OPCUA_EXCLUDE_MemoryBufferState)
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class MemoryBufferState : BaseObjectState
    {
        #region Constructors
        /// <remarks />
        public MemoryBufferState(NodeState parent) : base(parent)
        {
        }

        /// <remarks />
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(MemoryBuffer.ObjectTypes.MemoryBufferType, MemoryBuffer.Namespaces.MemoryBuffer, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <remarks />
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <remarks />
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            InitializeOptionalChildren(context);
            base.Initialize(context, source);
        }

        /// <remarks />
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACIAAABodHRwOi8vc2FtcGxlcy5vcmcvVUEvTWVtb3J5QnVmZmVy/////wRggAIBAAAAAQAYAAAA" +
           "TWVtb3J5QnVmZmVyVHlwZUluc3RhbmNlAQHoAwEB6APoAwAA/////wIAAAAVYKkKAgAAAAEADAAAAFN0" +
           "YXJ0QWRkcmVzcwEB6wMALgBE6wMAAAcAAAAAAAf/////AQH/////AAAAABVgqQoCAAAAAQALAAAAU2l6" +
           "ZUluQnl0ZXMBAewDAC4AROwDAAAHABAAAAAH/////wEB/////wAAAAA=";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <remarks />
        public PropertyState<uint> StartAddress
        {
            get
            {
                return m_startAddress;
            }

            set
            {
                if (!Object.ReferenceEquals(m_startAddress, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_startAddress = value;
            }
        }

        /// <remarks />
        public PropertyState<uint> SizeInBytes
        {
            get
            {
                return m_sizeInBytes;
            }

            set
            {
                if (!Object.ReferenceEquals(m_sizeInBytes, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_sizeInBytes = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <remarks />
        public override void GetChildren(
            ISystemContext context,
            IList<BaseInstanceState> children)
        {
            if (m_startAddress != null)
            {
                children.Add(m_startAddress);
            }

            if (m_sizeInBytes != null)
            {
                children.Add(m_sizeInBytes);
            }

            base.GetChildren(context, children);
        }
            
        /// <remarks />
        protected override BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName,
            bool createOrReplace,
            BaseInstanceState replacement)
        {
            if (QualifiedName.IsNull(browseName))
            {
                return null;
            }

            BaseInstanceState instance = null;

            switch (browseName.Name)
            {
                case MemoryBuffer.BrowseNames.StartAddress:
                {
                    if (createOrReplace)
                    {
                        if (StartAddress == null)
                        {
                            if (replacement == null)
                            {
                                StartAddress = new PropertyState<uint>(this);
                            }
                            else
                            {
                                StartAddress = (PropertyState<uint>)replacement;
                            }
                        }
                    }

                    instance = StartAddress;
                    break;
                }

                case MemoryBuffer.BrowseNames.SizeInBytes:
                {
                    if (createOrReplace)
                    {
                        if (SizeInBytes == null)
                        {
                            if (replacement == null)
                            {
                                SizeInBytes = new PropertyState<uint>(this);
                            }
                            else
                            {
                                SizeInBytes = (PropertyState<uint>)replacement;
                            }
                        }
                    }

                    instance = SizeInBytes;
                    break;
                }
            }

            if (instance != null)
            {
                return instance;
            }

            return base.FindChild(context, browseName, createOrReplace, replacement);
        }
        #endregion

        #region Private Fields
        private PropertyState<uint> m_startAddress;
        private PropertyState<uint> m_sizeInBytes;
        #endregion
    }
    #endif
    #endregion
}