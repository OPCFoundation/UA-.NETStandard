/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Di;
using Opc.Ua;

namespace Opc.Ua.Ws
{
    #region Method Identifiers
    /// <summary>
    /// A class that declares constants for all Methods in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Methods
    {
        /// <summary>
        /// The identifier for the WeightScaleType_Lock_InitLock Method.
        /// </summary>
        public const uint WeightScaleType_Lock_InitLock = 15283;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_RenewLock Method.
        /// </summary>
        public const uint WeightScaleType_Lock_RenewLock = 15286;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_ExitLock Method.
        /// </summary>
        public const uint WeightScaleType_Lock_ExitLock = 15288;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_BreakLock Method.
        /// </summary>
        public const uint WeightScaleType_Lock_BreakLock = 15290;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_InitLock Method.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_InitLock = 15318;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_RenewLock Method.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_RenewLock = 15321;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_ExitLock Method.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_ExitLock = 15323;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_BreakLock Method.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_BreakLock = 15325;

        /// <summary>
        /// The identifier for the WeightScaleType_MethodSet_Tare Method.
        /// </summary>
        public const uint WeightScaleType_MethodSet_Tare = 15345;

        /// <summary>
        /// The identifier for the WeightScaleType_MethodSet_Zero Method.
        /// </summary>
        public const uint WeightScaleType_MethodSet_Zero = 15346;
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
        /// The identifier for the WeightScaleType_ParameterSet Object.
        /// </summary>
        public const uint WeightScaleType_ParameterSet = 15270;

        /// <summary>
        /// The identifier for the WeightScaleType_MethodSet Object.
        /// </summary>
        public const uint WeightScaleType_MethodSet = 15272;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_NetworkAddress Object.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_NetworkAddress = 15327;
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
        /// The identifier for the WeightScaleType ObjectType.
        /// </summary>
        public const uint WeightScaleType = 15269;
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
        /// The identifier for the WeightScaleType_Lock_Locked Variable.
        /// </summary>
        public const uint WeightScaleType_Lock_Locked = 15279;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_LockingClient Variable.
        /// </summary>
        public const uint WeightScaleType_Lock_LockingClient = 15280;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_LockingUser Variable.
        /// </summary>
        public const uint WeightScaleType_Lock_LockingUser = 15281;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_RemainingLockTime Variable.
        /// </summary>
        public const uint WeightScaleType_Lock_RemainingLockTime = 15282;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_InitLock_InputArguments Variable.
        /// </summary>
        public const uint WeightScaleType_Lock_InitLock_InputArguments = 15284;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_InitLock_OutputArguments Variable.
        /// </summary>
        public const uint WeightScaleType_Lock_InitLock_OutputArguments = 15285;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_RenewLock_OutputArguments Variable.
        /// </summary>
        public const uint WeightScaleType_Lock_RenewLock_OutputArguments = 15287;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_ExitLock_OutputArguments Variable.
        /// </summary>
        public const uint WeightScaleType_Lock_ExitLock_OutputArguments = 15289;

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_BreakLock_OutputArguments Variable.
        /// </summary>
        public const uint WeightScaleType_Lock_BreakLock_OutputArguments = 15291;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_Locked Variable.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_Locked = 15314;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_LockingClient Variable.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_LockingClient = 15315;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_LockingUser Variable.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_LockingUser = 15316;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_RemainingLockTime Variable.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_RemainingLockTime = 15317;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_InitLock_InputArguments Variable.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_InitLock_InputArguments = 15319;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_InitLock_OutputArguments Variable.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_InitLock_OutputArguments = 15320;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_RenewLock_OutputArguments Variable.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_RenewLock_OutputArguments = 15322;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_ExitLock_OutputArguments Variable.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_ExitLock_OutputArguments = 15324;

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_BreakLock_OutputArguments Variable.
        /// </summary>
        public const uint WeightScaleType_CPIdentifier_Lock_BreakLock_OutputArguments = 15326;

        /// <summary>
        /// The identifier for the WeightScaleType_ParameterSet_weightScale Variable.
        /// </summary>
        public const uint WeightScaleType_ParameterSet_weightScale = 15339;

        /// <summary>
        /// The identifier for the WeightScaleType_ParameterSet_weightScale_InstrumentRange Variable.
        /// </summary>
        public const uint WeightScaleType_ParameterSet_weightScale_InstrumentRange = 15342;

        /// <summary>
        /// The identifier for the WeightScaleType_ParameterSet_weightScale_EURange Variable.
        /// </summary>
        public const uint WeightScaleType_ParameterSet_weightScale_EURange = 15343;

        /// <summary>
        /// The identifier for the WeightScaleType_ParameterSet_weightScale_EngineeringUnits Variable.
        /// </summary>
        public const uint WeightScaleType_ParameterSet_weightScale_EngineeringUnits = 15344;
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
        /// <summary>
        /// The identifier for the WeightScaleType_Lock_InitLock Method.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_InitLock = new ExpandedNodeId(Opc.Ua.Ws.Methods.WeightScaleType_Lock_InitLock, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_RenewLock Method.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.Ws.Methods.WeightScaleType_Lock_RenewLock, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_ExitLock Method.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.Ws.Methods.WeightScaleType_Lock_ExitLock, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_BreakLock Method.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.Ws.Methods.WeightScaleType_Lock_BreakLock, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_InitLock Method.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_InitLock = new ExpandedNodeId(Opc.Ua.Ws.Methods.WeightScaleType_CPIdentifier_Lock_InitLock, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_RenewLock Method.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_RenewLock = new ExpandedNodeId(Opc.Ua.Ws.Methods.WeightScaleType_CPIdentifier_Lock_RenewLock, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_ExitLock Method.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_ExitLock = new ExpandedNodeId(Opc.Ua.Ws.Methods.WeightScaleType_CPIdentifier_Lock_ExitLock, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_BreakLock Method.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_BreakLock = new ExpandedNodeId(Opc.Ua.Ws.Methods.WeightScaleType_CPIdentifier_Lock_BreakLock, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_MethodSet_Tare Method.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_MethodSet_Tare = new ExpandedNodeId(Opc.Ua.Ws.Methods.WeightScaleType_MethodSet_Tare, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_MethodSet_Zero Method.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_MethodSet_Zero = new ExpandedNodeId(Opc.Ua.Ws.Methods.WeightScaleType_MethodSet_Zero, Opc.Ua.Ws.Namespaces.OpcUaWs);
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
        /// The identifier for the WeightScaleType_ParameterSet Object.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_ParameterSet = new ExpandedNodeId(Opc.Ua.Ws.Objects.WeightScaleType_ParameterSet, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_MethodSet Object.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_MethodSet = new ExpandedNodeId(Opc.Ua.Ws.Objects.WeightScaleType_MethodSet, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_NetworkAddress Object.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_NetworkAddress = new ExpandedNodeId(Opc.Ua.Ws.Objects.WeightScaleType_CPIdentifier_NetworkAddress, Opc.Ua.Ws.Namespaces.OpcUaWs);
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
        /// The identifier for the WeightScaleType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType = new ExpandedNodeId(Opc.Ua.Ws.ObjectTypes.WeightScaleType, Opc.Ua.Ws.Namespaces.OpcUaWs);
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
        /// The identifier for the WeightScaleType_Lock_Locked Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_Locked = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_Lock_Locked, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_LockingClient Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_Lock_LockingClient, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_LockingUser Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_Lock_LockingUser, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_RemainingLockTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_Lock_RemainingLockTime, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_InitLock_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_Lock_InitLock_InputArguments, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_InitLock_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_Lock_InitLock_OutputArguments, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_RenewLock_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_Lock_RenewLock_OutputArguments, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_ExitLock_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_Lock_ExitLock_OutputArguments, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_Lock_BreakLock_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_Lock_BreakLock_OutputArguments, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_Locked Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_Locked = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_CPIdentifier_Lock_Locked, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_LockingClient Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_LockingClient = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_CPIdentifier_Lock_LockingClient, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_LockingUser Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_LockingUser = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_CPIdentifier_Lock_LockingUser, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_RemainingLockTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_RemainingLockTime = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_CPIdentifier_Lock_RemainingLockTime, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_InitLock_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_InitLock_InputArguments = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_CPIdentifier_Lock_InitLock_InputArguments, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_InitLock_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_InitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_CPIdentifier_Lock_InitLock_OutputArguments, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_RenewLock_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_RenewLock_OutputArguments = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_CPIdentifier_Lock_RenewLock_OutputArguments, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_ExitLock_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_ExitLock_OutputArguments = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_CPIdentifier_Lock_ExitLock_OutputArguments, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_CPIdentifier_Lock_BreakLock_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_CPIdentifier_Lock_BreakLock_OutputArguments = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_CPIdentifier_Lock_BreakLock_OutputArguments, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_ParameterSet_weightScale Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_ParameterSet_weightScale = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_ParameterSet_weightScale, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_ParameterSet_weightScale_InstrumentRange Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_ParameterSet_weightScale_InstrumentRange = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_ParameterSet_weightScale_InstrumentRange, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_ParameterSet_weightScale_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_ParameterSet_weightScale_EURange = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_ParameterSet_weightScale_EURange, Opc.Ua.Ws.Namespaces.OpcUaWs);

        /// <summary>
        /// The identifier for the WeightScaleType_ParameterSet_weightScale_EngineeringUnits Variable.
        /// </summary>
        public static readonly ExpandedNodeId WeightScaleType_ParameterSet_weightScale_EngineeringUnits = new ExpandedNodeId(Opc.Ua.Ws.Variables.WeightScaleType_ParameterSet_weightScale_EngineeringUnits, Opc.Ua.Ws.Namespaces.OpcUaWs);
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
        /// The BrowseName for the MethodSet component.
        /// </summary>
        public const string MethodSet = "MethodSet";

        /// <summary>
        /// The BrowseName for the ParameterSet component.
        /// </summary>
        public const string ParameterSet = "ParameterSet";

        /// <summary>
        /// The BrowseName for the WeightScaleType component.
        /// </summary>
        public const string WeightScaleType = "WeightScaleType";
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
        /// The URI for the OpcUaWs namespace (.NET code namespace is 'Opc.Ua.Ws').
        /// </summary>
        public const string OpcUaWs = "http://phi-ware.com/FEISTU/WS/";

        /// <summary>
        /// The URI for the OpcUaWsXsd namespace (.NET code namespace is 'Opc.Ua.Ws').
        /// </summary>
        public const string OpcUaWsXsd = "http://phi-ware.com/FEISTU/WS/Types.xsd";

        /// <summary>
        /// The URI for the OpcUaDi namespace (.NET code namespace is 'Opc.Ua.Di').
        /// </summary>
        public const string OpcUaDi = "http://opcfoundation.org/UA/DI/";

        /// <summary>
        /// The URI for the OpcUaDiXsd namespace (.NET code namespace is 'Opc.Ua.Di').
        /// </summary>
        public const string OpcUaDiXsd = "http://opcfoundation.org/UA/DI/Types.xsd";

        /// <summary>
        /// The URI for the OpcUa namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUa = "http://opcfoundation.org/UA/";

        /// <summary>
        /// The URI for the OpcUaXsd namespace (.NET code namespace is 'Opc.Ua').
        /// </summary>
        public const string OpcUaXsd = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    }
    #endregion
}