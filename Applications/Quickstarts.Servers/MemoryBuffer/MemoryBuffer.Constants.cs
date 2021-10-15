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
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
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
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
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
        public const string MemoryBuffer = "http://samples.org/UA/MemoryBuffer";
    }
    #endregion
}