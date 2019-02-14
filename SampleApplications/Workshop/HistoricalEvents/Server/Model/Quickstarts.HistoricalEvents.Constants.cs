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
        /// The identifier for the WellTestReportType_EventId Variable.
        /// </summary>
        public const uint WellTestReportType_EventId = 252;

        /// <summary>
        /// The identifier for the WellTestReportType_EventType Variable.
        /// </summary>
        public const uint WellTestReportType_EventType = 253;

        /// <summary>
        /// The identifier for the WellTestReportType_SourceNode Variable.
        /// </summary>
        public const uint WellTestReportType_SourceNode = 254;

        /// <summary>
        /// The identifier for the WellTestReportType_SourceName Variable.
        /// </summary>
        public const uint WellTestReportType_SourceName = 255;

        /// <summary>
        /// The identifier for the WellTestReportType_Time Variable.
        /// </summary>
        public const uint WellTestReportType_Time = 256;

        /// <summary>
        /// The identifier for the WellTestReportType_ReceiveTime Variable.
        /// </summary>
        public const uint WellTestReportType_ReceiveTime = 257;

        /// <summary>
        /// The identifier for the WellTestReportType_LocalTime Variable.
        /// </summary>
        public const uint WellTestReportType_LocalTime = 258;

        /// <summary>
        /// The identifier for the WellTestReportType_Message Variable.
        /// </summary>
        public const uint WellTestReportType_Message = 259;

        /// <summary>
        /// The identifier for the WellTestReportType_Severity Variable.
        /// </summary>
        public const uint WellTestReportType_Severity = 260;

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
        /// The identifier for the FluidLevelTestReportType_EventId Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_EventId = 266;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_EventType Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_EventType = 267;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_SourceNode Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_SourceNode = 268;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_SourceName Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_SourceName = 269;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_Time Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_Time = 270;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_ReceiveTime Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_ReceiveTime = 271;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_LocalTime Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_LocalTime = 272;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_Message Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_Message = 273;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_Severity Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_Severity = 274;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_NameWell Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_NameWell = 275;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_UidWell Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_UidWell = 276;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_TestDate Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_TestDate = 277;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_TestReason Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_TestReason = 278;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_FluidLevel = 279;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_Definition Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_FluidLevel_Definition = 280;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_ValuePrecision Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_FluidLevel_ValuePrecision = 281;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_EURange Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_FluidLevel_EURange = 304;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_InstrumentRange Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_FluidLevel_InstrumentRange = 305;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_EngineeringUnits Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_FluidLevel_EngineeringUnits = 282;

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_TestedBy Variable.
        /// </summary>
        public const uint FluidLevelTestReportType_TestedBy = 283;

        /// <summary>
        /// The identifier for the InjectionTestReportType_EventId Variable.
        /// </summary>
        public const uint InjectionTestReportType_EventId = 285;

        /// <summary>
        /// The identifier for the InjectionTestReportType_EventType Variable.
        /// </summary>
        public const uint InjectionTestReportType_EventType = 286;

        /// <summary>
        /// The identifier for the InjectionTestReportType_SourceNode Variable.
        /// </summary>
        public const uint InjectionTestReportType_SourceNode = 287;

        /// <summary>
        /// The identifier for the InjectionTestReportType_SourceName Variable.
        /// </summary>
        public const uint InjectionTestReportType_SourceName = 288;

        /// <summary>
        /// The identifier for the InjectionTestReportType_Time Variable.
        /// </summary>
        public const uint InjectionTestReportType_Time = 289;

        /// <summary>
        /// The identifier for the InjectionTestReportType_ReceiveTime Variable.
        /// </summary>
        public const uint InjectionTestReportType_ReceiveTime = 290;

        /// <summary>
        /// The identifier for the InjectionTestReportType_LocalTime Variable.
        /// </summary>
        public const uint InjectionTestReportType_LocalTime = 291;

        /// <summary>
        /// The identifier for the InjectionTestReportType_Message Variable.
        /// </summary>
        public const uint InjectionTestReportType_Message = 292;

        /// <summary>
        /// The identifier for the InjectionTestReportType_Severity Variable.
        /// </summary>
        public const uint InjectionTestReportType_Severity = 293;

        /// <summary>
        /// The identifier for the InjectionTestReportType_NameWell Variable.
        /// </summary>
        public const uint InjectionTestReportType_NameWell = 294;

        /// <summary>
        /// The identifier for the InjectionTestReportType_UidWell Variable.
        /// </summary>
        public const uint InjectionTestReportType_UidWell = 295;

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDate Variable.
        /// </summary>
        public const uint InjectionTestReportType_TestDate = 296;

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestReason Variable.
        /// </summary>
        public const uint InjectionTestReportType_TestReason = 297;

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration Variable.
        /// </summary>
        public const uint InjectionTestReportType_TestDuration = 298;

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_Definition Variable.
        /// </summary>
        public const uint InjectionTestReportType_TestDuration_Definition = 299;

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_ValuePrecision Variable.
        /// </summary>
        public const uint InjectionTestReportType_TestDuration_ValuePrecision = 300;

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_EURange Variable.
        /// </summary>
        public const uint InjectionTestReportType_TestDuration_EURange = 306;

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_InstrumentRange Variable.
        /// </summary>
        public const uint InjectionTestReportType_TestDuration_InstrumentRange = 307;

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
        /// The identifier for the WellTestReportType_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_EventId = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_EventId, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_EventType = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_EventType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_SourceNode = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_SourceNode, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_SourceName = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_SourceName, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_Time = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_Time, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_ReceiveTime = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_ReceiveTime, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_LocalTime = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_LocalTime, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_Message = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_Message, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the WellTestReportType_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId WellTestReportType_Severity = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.WellTestReportType_Severity, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

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
        /// The identifier for the FluidLevelTestReportType_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_EventId = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_EventId, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_EventType = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_EventType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_SourceNode = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_SourceNode, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_SourceName = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_SourceName, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_Time = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_Time, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_ReceiveTime = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_ReceiveTime, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_LocalTime = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_LocalTime, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_Message = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_Message, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_Severity = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_Severity, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_NameWell Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_NameWell = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_NameWell, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_UidWell Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_UidWell = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_UidWell, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_TestDate Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_TestDate = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_TestDate, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_TestReason Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_TestReason = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_TestReason, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_FluidLevel = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_FluidLevel, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_Definition Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_FluidLevel_Definition = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_FluidLevel_Definition, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_ValuePrecision Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_FluidLevel_ValuePrecision = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_FluidLevel_ValuePrecision, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_FluidLevel_EURange = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_FluidLevel_EURange, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_InstrumentRange Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_FluidLevel_InstrumentRange = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_FluidLevel_InstrumentRange, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_FluidLevel_EngineeringUnits Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_FluidLevel_EngineeringUnits = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_FluidLevel_EngineeringUnits, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the FluidLevelTestReportType_TestedBy Variable.
        /// </summary>
        public static readonly ExpandedNodeId FluidLevelTestReportType_TestedBy = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.FluidLevelTestReportType_TestedBy, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_EventId = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_EventId, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_EventType = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_EventType, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_SourceNode = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_SourceNode, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_SourceName = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_SourceName, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_Time = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_Time, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_ReceiveTime = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_ReceiveTime, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_LocalTime = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_LocalTime, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_Message = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_Message, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_Severity = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_Severity, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_NameWell Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_NameWell = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_NameWell, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_UidWell Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_UidWell = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_UidWell, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDate Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_TestDate = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_TestDate, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestReason Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_TestReason = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_TestReason, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_TestDuration = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_TestDuration, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_Definition Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_TestDuration_Definition = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_TestDuration_Definition, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_ValuePrecision Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_TestDuration_ValuePrecision = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_TestDuration_ValuePrecision, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_TestDuration_EURange = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_TestDuration_EURange, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

        /// <summary>
        /// The identifier for the InjectionTestReportType_TestDuration_InstrumentRange Variable.
        /// </summary>
        public static readonly ExpandedNodeId InjectionTestReportType_TestDuration_InstrumentRange = new ExpandedNodeId(Quickstarts.HistoricalEvents.Variables.InjectionTestReportType_TestDuration_InstrumentRange, Quickstarts.HistoricalEvents.Namespaces.HistoricalEvents);

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

        /// <summary>
        /// Returns a namespace table with all of the URIs defined.
        /// </summary>
        /// <remarks>
        /// This table is was used to create any relative paths in the model design.
        /// </remarks>
        public static NamespaceTable GetNamespaceTable()
        {
            FieldInfo[] fields = typeof(Namespaces).GetFields(BindingFlags.Public | BindingFlags.Static);

            NamespaceTable namespaceTable = new NamespaceTable();

            foreach (FieldInfo field in fields)
            {
                string namespaceUri = (string)field.GetValue(typeof(Namespaces));

                if (namespaceTable.GetIndex(namespaceUri) == -1)
                {
                    namespaceTable.Append(namespaceUri);
                }
            }

            return namespaceTable;
        }
    }
    #endregion
}
