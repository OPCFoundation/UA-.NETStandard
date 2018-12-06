/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.PubSub
{
    #region Method Identifiers
    /// <summary>
    /// A class that declares constants for all Methods in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Methods
    {
         
    }
    #endregion

    #region Object Identifiers
    /// <summary>
    /// A class that declares constants for all Objects in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <summary>
        /// The identifier for the OPCUANamespaceMetadata Object.
        /// </summary>
        public const uint OPCUANamespaceMetadata = 15957;

        /// <summary>
        /// The identifier for the WellKnownRole_SecurityAdmin Object.
        /// </summary>
        public const uint WellKnownRole_SecurityAdmin = 15704;

        /// <summary>
        /// The identifier for the WellKnownRole_ConfigureAdmin Object.
        /// </summary>
        public const uint WellKnownRole_ConfigureAdmin = 15716;

        /// <summary>
        /// The identifier for the WellKnownRole_Anonymous Object.
        /// </summary>
        public const uint WellKnownRole_Anonymous = 15644;

        /// <summary>
        /// The identifier for the WellKnownRole_AuthenticatedUser Object.
        /// </summary>
        public const uint WellKnownRole_AuthenticatedUser = 15656;

        /// <summary>
        /// The identifier for the WellKnownRole_Observer Object.
        /// </summary>
        public const uint WellKnownRole_Observer = 15668;

        /// <summary>
        /// The identifier for the WellKnownRole_Operator Object.
        /// </summary>
        public const uint WellKnownRole_Operator = 15680;

        /// <summary>
        /// The identifier for the WellKnownRole_Engineer Object.
        /// </summary>
        public const uint WellKnownRole_Engineer = 16036;

        /// <summary>
        /// The identifier for the WellKnownRole_Supervisor Object.
        /// </summary>
        public const uint WellKnownRole_Supervisor = 15692;
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
        
    }
    #endregion

    #region ReferenceType Identifiers
    /// <summary>
    /// A class that declares constants for all ReferenceTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ReferenceTypes
    {
        
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
        /// The identifier for the OPCUANamespaceMetadata_DefaultRolePermissions Variable.
        /// </summary>
        public const uint OPCUANamespaceMetadata_DefaultRolePermissions = 16134;

        /// <summary>
        /// The identifier for the OPCUANamespaceMetadata_DefaultUserRolePermissions Variable.
        /// </summary>
        public const uint OPCUANamespaceMetadata_DefaultUserRolePermissions = 16135;

        /// <summary>
        /// The identifier for the OPCUANamespaceMetadata_DefaultAccessRestrictions Variable.
        /// </summary>
        public const uint OPCUANamespaceMetadata_DefaultAccessRestrictions = 16136;
    }
    #endregion

    #region Method Node Identifiers
    /// <summary>
    /// A class that declares constants for all Methods in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class MethodIds
    {
        
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
        /// The identifier for the OPCUANamespaceMetadata Object.
        /// </summary>
        public static readonly NodeId OPCUANamespaceMetadata = new NodeId(Opc.Ua.PubSub.Objects.OPCUANamespaceMetadata);

        /// <summary>
        /// The identifier for the OPCUANamespaceMetadata_DefaultRolePermissions Variable.
        /// </summary>
        public static readonly NodeId OPCUANamespaceMetadata_DefaultRolePermissions = new NodeId(Opc.Ua.PubSub.Variables.OPCUANamespaceMetadata_DefaultRolePermissions);

        /// <summary>
        /// The identifier for the OPCUANamespaceMetadata_DefaultUserRolePermissions Variable.
        /// </summary>
        public static readonly NodeId OPCUANamespaceMetadata_DefaultUserRolePermissions = new NodeId(Opc.Ua.PubSub.Variables.OPCUANamespaceMetadata_DefaultUserRolePermissions);

        /// <summary>
        /// The identifier for the OPCUANamespaceMetadata_DefaultAccessRestrictions Variable.
        /// </summary>
        public static readonly NodeId OPCUANamespaceMetadata_DefaultAccessRestrictions = new NodeId(Opc.Ua.PubSub.Variables.OPCUANamespaceMetadata_DefaultAccessRestrictions);

        /// <summary>
        /// The identifier for the WellKnownRole_SecurityAdmin Object.
        /// </summary>
        public static readonly NodeId WellKnownRole_SecurityAdmin = new NodeId(Opc.Ua.PubSub.Objects.WellKnownRole_SecurityAdmin);

        /// <summary>
        /// The identifier for the WellKnownRole_ConfigureAdmin Object.
        /// </summary>
        public static readonly NodeId WellKnownRole_ConfigureAdmin = new NodeId(Opc.Ua.PubSub.Objects.WellKnownRole_ConfigureAdmin);

        /// <summary>
        /// The identifier for the WellKnownRole_Anonymous Object.
        /// </summary>
        public static readonly NodeId WellKnownRole_Anonymous = new NodeId(Opc.Ua.PubSub.Objects.WellKnownRole_Anonymous);

        /// <summary>
        /// The identifier for the WellKnownRole_AuthenticatedUser Object.
        /// </summary>
        public static readonly NodeId WellKnownRole_AuthenticatedUser = new NodeId(Opc.Ua.PubSub.Objects.WellKnownRole_AuthenticatedUser);

        /// <summary>
        /// The identifier for the WellKnownRole_Observer Object.
        /// </summary>
        public static readonly NodeId WellKnownRole_Observer = new NodeId(Opc.Ua.PubSub.Objects.WellKnownRole_Observer);

        /// <summary>
        /// The identifier for the WellKnownRole_Operator Object.
        /// </summary>
        public static readonly NodeId WellKnownRole_Operator = new NodeId(Opc.Ua.PubSub.Objects.WellKnownRole_Operator);

        /// <summary>
        /// The identifier for the WellKnownRole_Engineer Object.
        /// </summary>
        public static readonly NodeId WellKnownRole_Engineer = new NodeId(Opc.Ua.PubSub.Objects.WellKnownRole_Engineer);

        /// <summary>
        /// The identifier for the WellKnownRole_Supervisor Object.
        /// </summary>
        public static readonly NodeId WellKnownRole_Supervisor = new NodeId(Opc.Ua.PubSub.Objects.WellKnownRole_Supervisor);


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
         
    }
    #endregion

    #region ReferenceType Node Identifiers
    /// <summary>
    /// A class that declares constants for all ReferenceTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ReferenceTypeIds
    {
         
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
        /// The identifier for the OPCUANamespaceMetadata_DefaultRolePermissions Variable.
        /// </summary>
        public static readonly NodeId OPCUANamespaceMetadata_DefaultRolePermissions = new NodeId(Opc.Ua.PubSub.Variables.OPCUANamespaceMetadata_DefaultRolePermissions);

        /// <summary>
        /// The identifier for the OPCUANamespaceMetadata_DefaultUserRolePermissions Variable.
        /// </summary>
        public static readonly NodeId OPCUANamespaceMetadata_DefaultUserRolePermissions = new NodeId(Opc.Ua.PubSub.Variables.OPCUANamespaceMetadata_DefaultUserRolePermissions);

        /// <summary>
        /// The identifier for the OPCUANamespaceMetadata_DefaultAccessRestrictions Variable.
        /// </summary>
        public static readonly NodeId OPCUANamespaceMetadata_DefaultAccessRestrictions = new NodeId(Opc.Ua.PubSub.Variables.OPCUANamespaceMetadata_DefaultAccessRestrictions);

    }
    #endregion

    #region BrowseName Declarations
    /// <summary>
    /// Declares all of the BrowseNames used in the Model Design.
    /// </summary>
    public static partial class BrowseNames
    {
        
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
        /// The URI for the OpcUaPubSub namespace (.NET code namespace is 'Opc.Ua.PubSub').
        /// </summary>
        public const string OpcUaPubSub = "http://opcfoundation.org/UA/PubSub/";
    }
    #endregion
}