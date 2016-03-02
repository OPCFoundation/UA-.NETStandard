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
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;
using Opc.Ua;

namespace MemoryBuffer
{
    #region Object Identifiers
    /// <summary>
    /// A class that declares constants for all Objects in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <summary>
        /// The identifier for the MemoryBuffers Object.
        /// </summary>
        public const uint MemoryBuffers = 1025;
    }
    #endregion

    #region ObjectType Identifiers
    /// <summary>
    /// A class that declares constants for all ObjectTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <summary>
        /// The identifier for the MemoryBufferType ObjectType.
        /// </summary>
        public const uint MemoryBufferType = 1000;
    }
    #endregion

    #region Variable Identifiers
    /// <summary>
    /// A class that declares constants for all Variables in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <summary>
        /// The identifier for the MemoryBufferType_StartAddress Variable.
        /// </summary>
        public const uint MemoryBufferType_StartAddress = 1003;

        /// <summary>
        /// The identifier for the MemoryBufferType_SizeInBytes Variable.
        /// </summary>
        public const uint MemoryBufferType_SizeInBytes = 1004;
    }
    #endregion

    #region VariableType Identifiers
    /// <summary>
    /// A class that declares constants for all VariableTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableTypes
    {
        /// <summary>
        /// The identifier for the MemoryTagType VariableType.
        /// </summary>
        public const uint MemoryTagType = 1018;
    }
    #endregion

    #region Object Node Identifiers
    /// <summary>
    /// A class that declares constants for all Objects in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <summary>
        /// The identifier for the MemoryBuffers Object.
        /// </summary>
        public static readonly ExpandedNodeId MemoryBuffers = new ExpandedNodeId(MemoryBuffer.Objects.MemoryBuffers, MemoryBuffer.Namespaces.MemoryBuffer);
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <summary>
    /// A class that declares constants for all ObjectTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <summary>
        /// The identifier for the MemoryBufferType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId MemoryBufferType = new ExpandedNodeId(MemoryBuffer.ObjectTypes.MemoryBufferType, MemoryBuffer.Namespaces.MemoryBuffer);
    }
    #endregion

    #region Variable Node Identifiers
    /// <summary>
    /// A class that declares constants for all Variables in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <summary>
        /// The identifier for the MemoryBufferType_StartAddress Variable.
        /// </summary>
        public static readonly ExpandedNodeId MemoryBufferType_StartAddress = new ExpandedNodeId(MemoryBuffer.Variables.MemoryBufferType_StartAddress, MemoryBuffer.Namespaces.MemoryBuffer);

        /// <summary>
        /// The identifier for the MemoryBufferType_SizeInBytes Variable.
        /// </summary>
        public static readonly ExpandedNodeId MemoryBufferType_SizeInBytes = new ExpandedNodeId(MemoryBuffer.Variables.MemoryBufferType_SizeInBytes, MemoryBuffer.Namespaces.MemoryBuffer);
    }
    #endregion

    #region VariableType Node Identifiers
    /// <summary>
    /// A class that declares constants for all VariableTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableTypeIds
    {
        /// <summary>
        /// The identifier for the MemoryTagType VariableType.
        /// </summary>
        public static readonly ExpandedNodeId MemoryTagType = new ExpandedNodeId(MemoryBuffer.VariableTypes.MemoryTagType, MemoryBuffer.Namespaces.MemoryBuffer);
    }
    #endregion

    #region BrowseName Declarations
    /// <summary>
    /// Declares all of the BrowseNames used in the Model Design.
    /// </summary>
    public static partial class BrowseNames
    {
        /// <summary>
        /// The BrowseName for the MemoryBuffers component.
        /// </summary>
        public const string MemoryBuffers = "MemoryBuffers";

        /// <summary>
        /// The BrowseName for the MemoryBufferType component.
        /// </summary>
        public const string MemoryBufferType = "MemoryBufferType";

        /// <summary>
        /// The BrowseName for the MemoryTagType component.
        /// </summary>
        public const string MemoryTagType = "MemoryTagType";

        /// <summary>
        /// The BrowseName for the SizeInBytes component.
        /// </summary>
        public const string SizeInBytes = "SizeInBytes";

        /// <summary>
        /// The BrowseName for the StartAddress component.
        /// </summary>
        public const string StartAddress = "StartAddress";
    }
    #endregion

    #region Namespace Declarations
    /// <summary>
    /// Defines constants for all namespaces referenced by the model design.
    /// </summary>
    public static partial class Namespaces
    {
        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";

        /// <summary>
        /// The URI for the MemoryBuffer namespace (.NET code namespace is 'MemoryBuffer').
        /// </summary>
        public const string MemoryBuffer = "http://samples.org/UA/memorybuffer";
    }
    #endregion

    #region MemoryTagState Class
    #if (!OPCUA_EXCLUDE_MemoryTagState)
    /// <summary>
    /// Stores an instance of the MemoryTagType VariableType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class MemoryTagState : BaseDataVariableState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public MemoryTagState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(MemoryBuffer.VariableTypes.MemoryTagType, MemoryBuffer.Namespaces.MemoryBuffer, namespaceUris);
        }

        /// <summary>
        /// Returns the id of the default data type node for the instance.
        /// </summary>
        protected override NodeId GetDefaultDataTypeId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(Opc.Ua.DataTypes.BaseDataType, Opc.Ua.Namespaces.OpcUa, namespaceUris);
        }

        /// <summary>
        /// Returns the id of the default value rank for the instance.
        /// </summary>
        protected override int GetDefaultValueRank()
        {
            return ValueRanks.Scalar;
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACIAAABodHRwOi8vc2FtcGxlcy5vcmcvVUEvbWVtb3J5YnVmZmVy/////xVggQACAAAAAQAVAAAA" +
           "TWVtb3J5VGFnVHlwZUluc3RhbmNlAQH6AwEB+gMAGAEB/////wAAAAA=";
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
    /// <summary>
    /// A typed version of the MemoryTagType variable.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public class MemoryTagState<T> : MemoryTagState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public MemoryTagState(NodeState parent) : base(parent)
        {
            Value = default(T);
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);

            Value = default(T);
            DataType = Opc.Ua.TypeInfo.GetDataTypeId(typeof(T));
            ValueRank = Opc.Ua.TypeInfo.GetValueRank(typeof(T));
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The value of the variable.
        /// </summary>
        public new T Value
        {
            get
            {
                return CheckTypeBeforeCast<T>(base.Value, true);
            }

            set
            {
                base.Value = value;
            }
        }
        #endregion
    }
    #endregion
    #endif
    #endregion

    #region MemoryBufferState Class
    #if (!OPCUA_EXCLUDE_MemoryBufferState)
    /// <summary>
    /// Stores an instance of the MemoryBufferType ObjectType.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public partial class MemoryBufferState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public MemoryBufferState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return Opc.Ua.NodeId.Create(MemoryBuffer.ObjectTypes.MemoryBufferType, MemoryBuffer.Namespaces.MemoryBuffer, namespaceUris);
        }

        #if (!OPCUA_EXCLUDE_InitializationStrings)
        /// <summary>
        /// Initializes the instance.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            Initialize(context, InitializationString);
            InitializeOptionalChildren(context);
        }

        /// <summary>
        /// Initializes the any option children defined for the instance.
        /// </summary>
        protected override void InitializeOptionalChildren(ISystemContext context)
        {
            base.InitializeOptionalChildren(context);
        }

        #region Initialization String
        private const string InitializationString =
           "AQAAACIAAABodHRwOi8vc2FtcGxlcy5vcmcvVUEvbWVtb3J5YnVmZmVy/////wRggAABAAAAAQAYAAAA" +
           "TWVtb3J5QnVmZmVyVHlwZUluc3RhbmNlAQHoAwEB6AP/////AgAAABVgqQoCAAAAAQAMAAAAU3RhcnRB" +
           "ZGRyZXNzAQHrAwAuAETrAwAABwAAAAAAB/////8BAf////8AAAAAFWCpCgIAAAABAAsAAABTaXplSW5C" +
           "eXRlcwEB7AMALgBE7AMAAAcAEAAAAAf/////AQH/////AAAAAA==";
        #endregion
        #endif
        #endregion

        #region Public Properties
        /// <summary>
        /// A description for the StartAddress Property.
        /// </summary>
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

        /// <summary>
        /// A description for the SizeInBytes Property.
        /// </summary>
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
        /// <summary>
        /// Populates a list with the children that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="children">The list of children to populate.</param>
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

        /// <summary>
        /// Finds the child with the specified browse name.
        /// </summary>
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