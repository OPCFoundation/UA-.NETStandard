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
using Opc.Ua;

namespace Quickstarts.HistoricalEvents
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
        /// The identifier for the Plaforms Object.
        /// </summary>
        public const uint Plaforms = 303;
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
        /// The identifier for the WellTestReportType ObjectType.
        /// </summary>
        public const uint WellTestReportType = 251;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType ObjectType.
        /// </summary>
        public const uint FluidLevelTestReportType = 265;

        /// <summary>
        /// The identifier for the InjectionTestReportType ObjectType.
        /// </summary>
        public const uint InjectionTestReportType = 284;

        /// <summary>
        /// The identifier for the WellType ObjectType.
        /// </summary>
        public const uint WellType = 308;
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
        /// The identifier for the WellTestReportType_NameWell Variable.
        /// </summary>
        public const uint WellTestReportType_NameWell = 261;

        /// <summary>
        /// The identifier for the WellTestReportType_UidWell Variable.
        /// </summary>
        public const uint WellTestReportType_UidWell = 262;

        /// <summary>
        /// The identifier for the WellTestReportType_TestDate Variable.
        /// </summary>
        public const uint WellTestReportType_TestDate = 263;

        /// <summary>
        /// The identifier for the WellTestReportType_TestReason Variable.
        /// </summary>
        public const uint WellTestReportType_TestReason = 264;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_FluidLevel = 279;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_EURange Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_FluidLevel_EURange = 304;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_EngineeringUnits Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_FluidLevel_EngineeringUnits = 282;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_TestedBy Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_TestedBy = 283;

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration Variable.
        /// </summary>
        public const uint InjectionTestReportType_TestDuration = 298;

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_EURange Variable.
        /// </summary>
        public const uint InjectionTestReportType_TestDuration_EURange = 306;

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_EngineeringUnits Variable.
        /// </summary>
        public const uint InjectionTestReportType_TestDuration_EngineeringUnits = 301;

        /// <summary>
        /// The identifier for the InjectionTestReportType_InjectedFluid Variable.
        /// </summary>
        public const uint InjectionTestReportType_InjectedFluid = 302;
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
        /// The identifier for the Plaforms Object.
        /// </summary>
        public static readonly ExpandedNodeId Plaforms = new ExpandedNodeId(Quickstarts.HistoricalEvents.Objects.Plaforms, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);
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
        /// The identifier for the WellTestReportType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType = new ExpandedNodeId(Quickstarts.HistoricalEvents.ObjectTypes.WellTestReportType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType = new ExpandedNodeId(Quickstarts.HistoricalEvents.ObjectTypes.FluidLevelTestReportType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType = new ExpandedNodeId(Quickstarts.HistoricalEvents.ObjectTypes.InjectionTestReportType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId WellType = new ExpandedNodeId(Quickstarts.HistoricalEvents.ObjectTypes.WellType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);
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
        /// The identifier for the WellTestReportType_NameWell Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_NameWell = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_NameWell, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_UidWell Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_UidWell = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_UidWell, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_TestDate Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_TestDate = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_TestDate, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_TestReason Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_TestReason = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_TestReason, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_FluidLevel = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_FluidLevel, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_FluidLevel_EURange = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_FluidLevel_EURange, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_EngineeringUnits Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_FluidLevel_EngineeringUnits = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_FluidLevel_EngineeringUnits, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_TestedBy Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_TestedBy = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_TestedBy, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_TestDuration = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_TestDuration, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_TestDuration_EURange = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_TestDuration_EURange, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_EngineeringUnits Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_TestDuration_EngineeringUnits = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_TestDuration_EngineeringUnits, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_InjectedFluid Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_InjectedFluid = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_InjectedFluid, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);
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
        /// The BrowseName for the FluidLevel component.
        /// </summary>
        public const string FluidLevel = "FluidLevel";

        /// <summary>
        /// The BrowseName for the FluidLevelTestReportType component.
        /// </summary>
        public const string FluidLevelTestReportType = "FluidLevelTestReportType";

        /// <summary>
        /// The BrowseName for the InjectedFluid component.
        /// </summary>
        public const string InjectedFluid = "InjectedFluid";

        /// <summary>
        /// The BrowseName for the InjectionTestReportType component.
        /// </summary>
        public const string InjectionTestReportType = "InjectionTestReportType";

        /// <summary>
        /// The BrowseName for the NameWell component.
        /// </summary>
        public const string NameWell = "NameWell";

        /// <summary>
        /// The BrowseName for the Plaforms component.
        /// </summary>
        public const string Plaforms = "Plaforms";

        /// <summary>
        /// The BrowseName for the TestDate component.
        /// </summary>
        public const string TestDate = "TestDate";

        /// <summary>
        /// The BrowseName for the TestDuration component.
        /// </summary>
        public const string TestDuration = "TestDuration";

        /// <summary>
        /// The BrowseName for the TestedBy component.
        /// </summary>
        public const string TestedBy = "TestedBy";

        /// <summary>
        /// The BrowseName for the TestReason component.
        /// </summary>
        public const string TestReason = "TestReason";

        /// <summary>
        /// The BrowseName for the UidWell component.
        /// </summary>
        public const string UidWell = "UidWell";

        /// <summary>
        /// The BrowseName for the WellTestReportType component.
        /// </summary>
        public const string WellTestReportType = "WellTestReportType";

        /// <summary>
        /// The BrowseName for the WellType component.
        /// </summary>
        public const string WellType = "WellType";
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
        /// The URI for the HistoricalEvents namespace (.NET code namespace is 'Quickstarts.HistoricalEvents').
        /// </summary>
        public const string HistoricalEvents = "http://opcfoundation.org/Quickstarts/HistoricalEvents";
    }
    #endregion
}