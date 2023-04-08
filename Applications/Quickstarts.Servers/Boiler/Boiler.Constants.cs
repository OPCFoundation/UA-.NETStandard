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

namespace Boiler
{
    #region Method Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Methods
    {
        /// <remarks />
        public const uint BoilerStateMachineType_Start = 1095;

        /// <remarks />
        public const uint BoilerStateMachineType_Suspend = 1096;

        /// <remarks />
        public const uint BoilerStateMachineType_Resume = 1097;

        /// <remarks />
        public const uint BoilerStateMachineType_Halt = 1098;

        /// <remarks />
        public const uint BoilerStateMachineType_Reset = 1099;

        /// <remarks />
        public const uint BoilerType_Simulation_Start = 15013;

        /// <remarks />
        public const uint BoilerType_Simulation_Suspend = 15014;

        /// <remarks />
        public const uint BoilerType_Simulation_Resume = 15015;

        /// <remarks />
        public const uint BoilerType_Simulation_Halt = 15016;

        /// <remarks />
        public const uint BoilerType_Simulation_Reset = 15017;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Start = 15018;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Suspend = 15019;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Resume = 15020;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Halt = 15021;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Reset = 15022;
    }
    #endregion

    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint BoilerInputPipeType_FlowTransmitter1 = 1102;

        /// <remarks />
        public const uint BoilerInputPipeType_Valve = 1109;

        /// <remarks />
        public const uint BoilerDrumType_LevelIndicator = 1117;

        /// <remarks />
        public const uint BoilerOutputPipeType_FlowTransmitter2 = 1125;

        /// <remarks />
        public const uint BoilerType_InputPipe = 1133;

        /// <remarks />
        public const uint BoilerType_InputPipe_FlowTransmitter1 = 1134;

        /// <remarks />
        public const uint BoilerType_InputPipe_Valve = 1141;

        /// <remarks />
        public const uint BoilerType_Drum = 1148;

        /// <remarks />
        public const uint BoilerType_Drum_LevelIndicator = 1149;

        /// <remarks />
        public const uint BoilerType_OutputPipe = 1156;

        /// <remarks />
        public const uint BoilerType_OutputPipe_FlowTransmitter2 = 1157;

        /// <remarks />
        public const uint BoilerType_FlowController = 1164;

        /// <remarks />
        public const uint BoilerType_LevelController = 1168;

        /// <remarks />
        public const uint BoilerType_CustomController = 1172;

        /// <remarks />
        public const uint BoilerType_Simulation = 1178;

        /// <remarks />
        public const uint Boilers = 1240;

        /// <remarks />
        public const uint Boilers_Boiler1 = 1241;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe = 1242;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_FlowTransmitter1 = 1243;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_Valve = 1250;

        /// <remarks />
        public const uint Boilers_Boiler1_Drum = 1257;

        /// <remarks />
        public const uint Boilers_Boiler1_Drum_LevelIndicator = 1258;

        /// <remarks />
        public const uint Boilers_Boiler1_OutputPipe = 1265;

        /// <remarks />
        public const uint Boilers_Boiler1_OutputPipe_FlowTransmitter2 = 1266;

        /// <remarks />
        public const uint Boilers_Boiler1_FlowController = 1273;

        /// <remarks />
        public const uint Boilers_Boiler1_LevelController = 1277;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController = 1281;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation = 1287;
    }
    #endregion

    #region ObjectType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <remarks />
        public const uint GenericControllerType = 210;

        /// <remarks />
        public const uint GenericSensorType = 991;

        /// <remarks />
        public const uint GenericActuatorType = 998;

        /// <remarks />
        public const uint CustomControllerType = 513;

        /// <remarks />
        public const uint ValveType = 1010;

        /// <remarks />
        public const uint LevelControllerType = 1017;

        /// <remarks />
        public const uint FlowControllerType = 1021;

        /// <remarks />
        public const uint LevelIndicatorType = 1025;

        /// <remarks />
        public const uint FlowTransmitterType = 1032;

        /// <remarks />
        public const uint BoilerStateMachineType = 1039;

        /// <remarks />
        public const uint BoilerInputPipeType = 1101;

        /// <remarks />
        public const uint BoilerDrumType = 1116;

        /// <remarks />
        public const uint BoilerOutputPipeType = 1124;

        /// <remarks />
        public const uint BoilerType = 1132;
    }
    #endregion

    #region ReferenceType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ReferenceTypes
    {
        /// <remarks />
        public const uint FlowTo = 985;

        /// <remarks />
        public const uint HotFlowTo = 986;

        /// <remarks />
        public const uint SignalTo = 987;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint GenericControllerType_Measurement = 988;

        /// <remarks />
        public const uint GenericControllerType_SetPoint = 989;

        /// <remarks />
        public const uint GenericControllerType_ControlOut = 990;

        /// <remarks />
        public const uint GenericSensorType_Output = 992;

        /// <remarks />
        public const uint GenericSensorType_Output_EURange = 995;

        /// <remarks />
        public const uint GenericActuatorType_Input = 999;

        /// <remarks />
        public const uint GenericActuatorType_Input_EURange = 1002;

        /// <remarks />
        public const uint CustomControllerType_Input1 = 1005;

        /// <remarks />
        public const uint CustomControllerType_Input2 = 1006;

        /// <remarks />
        public const uint CustomControllerType_Input3 = 1007;

        /// <remarks />
        public const uint CustomControllerType_ControlOut = 1008;

        /// <remarks />
        public const uint CustomControllerType_DescriptionX = 1009;

        /// <remarks />
        public const uint ValveType_Input_EURange = 1014;

        /// <remarks />
        public const uint LevelIndicatorType_Output_EURange = 1029;

        /// <remarks />
        public const uint FlowTransmitterType_Output_EURange = 1036;

        /// <remarks />
        public const uint BoilerStateMachineType_CurrentState_Id = 1041;

        /// <remarks />
        public const uint BoilerStateMachineType_CurrentState_Number = 1043;

        /// <remarks />
        public const uint BoilerStateMachineType_LastTransition_Id = 1046;

        /// <remarks />
        public const uint BoilerStateMachineType_LastTransition_Number = 1048;

        /// <remarks />
        public const uint BoilerStateMachineType_LastTransition_TransitionTime = 1049;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_CreateSessionId = 15024;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_CreateClientName = 15025;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_InvocationCreationTime = 15026;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastTransitionTime = 15027;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodCall = 15028;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodSessionId = 15029;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodInputArguments = 15030;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodOutputArguments = 15031;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodInputValues = 15032;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodOutputValues = 15033;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodCallTime = 15034;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodReturnStatus = 15035;

        /// <remarks />
        public const uint BoilerStateMachineType_Halted_StateNumber = 1076;

        /// <remarks />
        public const uint BoilerStateMachineType_Ready_StateNumber = 1070;

        /// <remarks />
        public const uint BoilerStateMachineType_Running_StateNumber = 1072;

        /// <remarks />
        public const uint BoilerStateMachineType_Suspended_StateNumber = 1074;

        /// <remarks />
        public const uint BoilerStateMachineType_HaltedToReady_TransitionNumber = 1078;

        /// <remarks />
        public const uint BoilerStateMachineType_ReadyToRunning_TransitionNumber = 1080;

        /// <remarks />
        public const uint BoilerStateMachineType_RunningToHalted_TransitionNumber = 1082;

        /// <remarks />
        public const uint BoilerStateMachineType_RunningToReady_TransitionNumber = 1084;

        /// <remarks />
        public const uint BoilerStateMachineType_RunningToSuspended_TransitionNumber = 1086;

        /// <remarks />
        public const uint BoilerStateMachineType_SuspendedToRunning_TransitionNumber = 1088;

        /// <remarks />
        public const uint BoilerStateMachineType_SuspendedToHalted_TransitionNumber = 1090;

        /// <remarks />
        public const uint BoilerStateMachineType_SuspendedToReady_TransitionNumber = 1092;

        /// <remarks />
        public const uint BoilerStateMachineType_ReadyToHalted_TransitionNumber = 1094;

        /// <remarks />
        public const uint BoilerStateMachineType_UpdateRate = 1100;

        /// <remarks />
        public const uint BoilerInputPipeType_FlowTransmitter1_Output = 1103;

        /// <remarks />
        public const uint BoilerInputPipeType_FlowTransmitter1_Output_EURange = 1106;

        /// <remarks />
        public const uint BoilerInputPipeType_Valve_Input = 1110;

        /// <remarks />
        public const uint BoilerInputPipeType_Valve_Input_EURange = 1113;

        /// <remarks />
        public const uint BoilerDrumType_LevelIndicator_Output = 1118;

        /// <remarks />
        public const uint BoilerDrumType_LevelIndicator_Output_EURange = 1121;

        /// <remarks />
        public const uint BoilerOutputPipeType_FlowTransmitter2_Output = 1126;

        /// <remarks />
        public const uint BoilerOutputPipeType_FlowTransmitter2_Output_EURange = 1129;

        /// <remarks />
        public const uint BoilerType_InputPipe_FlowTransmitter1_Output = 1135;

        /// <remarks />
        public const uint BoilerType_InputPipe_FlowTransmitter1_Output_EURange = 1138;

        /// <remarks />
        public const uint BoilerType_InputPipe_Valve_Input = 1142;

        /// <remarks />
        public const uint BoilerType_InputPipe_Valve_Input_EURange = 1145;

        /// <remarks />
        public const uint BoilerType_Drum_LevelIndicator_Output = 1150;

        /// <remarks />
        public const uint BoilerType_Drum_LevelIndicator_Output_EURange = 1153;

        /// <remarks />
        public const uint BoilerType_OutputPipe_FlowTransmitter2_Output = 1158;

        /// <remarks />
        public const uint BoilerType_OutputPipe_FlowTransmitter2_Output_EURange = 1161;

        /// <remarks />
        public const uint BoilerType_FlowController_Measurement = 1165;

        /// <remarks />
        public const uint BoilerType_FlowController_SetPoint = 1166;

        /// <remarks />
        public const uint BoilerType_FlowController_ControlOut = 1167;

        /// <remarks />
        public const uint BoilerType_LevelController_Measurement = 1169;

        /// <remarks />
        public const uint BoilerType_LevelController_SetPoint = 1170;

        /// <remarks />
        public const uint BoilerType_LevelController_ControlOut = 1171;

        /// <remarks />
        public const uint BoilerType_CustomController_Input1 = 1173;

        /// <remarks />
        public const uint BoilerType_CustomController_Input2 = 1174;

        /// <remarks />
        public const uint BoilerType_CustomController_Input3 = 1175;

        /// <remarks />
        public const uint BoilerType_CustomController_ControlOut = 1176;

        /// <remarks />
        public const uint BoilerType_CustomController_DescriptionX = 1177;

        /// <remarks />
        public const uint BoilerType_Simulation_CurrentState = 1179;

        /// <remarks />
        public const uint BoilerType_Simulation_CurrentState_Id = 1180;

        /// <remarks />
        public const uint BoilerType_Simulation_CurrentState_Number = 1182;

        /// <remarks />
        public const uint BoilerType_Simulation_LastTransition = 1184;

        /// <remarks />
        public const uint BoilerType_Simulation_LastTransition_Id = 1185;

        /// <remarks />
        public const uint BoilerType_Simulation_LastTransition_Number = 1187;

        /// <remarks />
        public const uint BoilerType_Simulation_LastTransition_TransitionTime = 1188;

        /// <remarks />
        public const uint BoilerType_Simulation_Deletable = 1190;

        /// <remarks />
        public const uint BoilerType_Simulation_AutoDelete = 1191;

        /// <remarks />
        public const uint BoilerType_Simulation_RecycleCount = 1192;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_CreateSessionId = 15037;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_CreateClientName = 15038;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_InvocationCreationTime = 15039;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastTransitionTime = 15040;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodCall = 15041;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodSessionId = 15042;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodInputArguments = 15043;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodOutputArguments = 15044;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodInputValues = 15045;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodOutputValues = 15046;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodCallTime = 15047;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodReturnStatus = 15048;

        /// <remarks />
        public const uint BoilerType_Simulation_UpdateRate = 1239;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_FlowTransmitter1_Output = 1244;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_FlowTransmitter1_Output_EURange = 1247;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_Valve_Input = 1251;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_Valve_Input_EURange = 1254;

        /// <remarks />
        public const uint Boilers_Boiler1_Drum_LevelIndicator_Output = 1259;

        /// <remarks />
        public const uint Boilers_Boiler1_Drum_LevelIndicator_Output_EURange = 1262;

        /// <remarks />
        public const uint Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output = 1267;

        /// <remarks />
        public const uint Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output_EURange = 1270;

        /// <remarks />
        public const uint Boilers_Boiler1_FlowController_Measurement = 1274;

        /// <remarks />
        public const uint Boilers_Boiler1_FlowController_SetPoint = 1275;

        /// <remarks />
        public const uint Boilers_Boiler1_FlowController_ControlOut = 1276;

        /// <remarks />
        public const uint Boilers_Boiler1_LevelController_Measurement = 1278;

        /// <remarks />
        public const uint Boilers_Boiler1_LevelController_SetPoint = 1279;

        /// <remarks />
        public const uint Boilers_Boiler1_LevelController_ControlOut = 1280;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController_Input1 = 1282;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController_Input2 = 1283;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController_Input3 = 1284;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController_ControlOut = 1285;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController_DescriptionX = 1286;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_CurrentState = 1288;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_CurrentState_Id = 1289;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_CurrentState_Number = 1291;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_LastTransition = 1293;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_LastTransition_Id = 1294;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_LastTransition_Number = 1296;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_LastTransition_TransitionTime = 1297;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Deletable = 1299;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_AutoDelete = 1300;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_RecycleCount = 1301;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_CreateSessionId = 15050;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_CreateClientName = 15051;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_InvocationCreationTime = 15052;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastTransitionTime = 15053;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodCall = 15054;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodSessionId = 15055;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodInputArguments = 15056;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodOutputArguments = 15057;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodInputValues = 15058;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodOutputValues = 15059;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodCallTime = 15060;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodReturnStatus = 15061;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_UpdateRate = 1348;
    }
    #endregion

    #region Method Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class MethodIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_Start = new ExpandedNodeId(Boiler.Methods.BoilerStateMachineType_Start, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_Suspend = new ExpandedNodeId(Boiler.Methods.BoilerStateMachineType_Suspend, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_Resume = new ExpandedNodeId(Boiler.Methods.BoilerStateMachineType_Resume, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_Halt = new ExpandedNodeId(Boiler.Methods.BoilerStateMachineType_Halt, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_Reset = new ExpandedNodeId(Boiler.Methods.BoilerStateMachineType_Reset, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_Start = new ExpandedNodeId(Boiler.Methods.BoilerType_Simulation_Start, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_Suspend = new ExpandedNodeId(Boiler.Methods.BoilerType_Simulation_Suspend, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_Resume = new ExpandedNodeId(Boiler.Methods.BoilerType_Simulation_Resume, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_Halt = new ExpandedNodeId(Boiler.Methods.BoilerType_Simulation_Halt, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_Reset = new ExpandedNodeId(Boiler.Methods.BoilerType_Simulation_Reset, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Start = new ExpandedNodeId(Boiler.Methods.Boilers_Boiler1_Simulation_Start, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Suspend = new ExpandedNodeId(Boiler.Methods.Boilers_Boiler1_Simulation_Suspend, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Resume = new ExpandedNodeId(Boiler.Methods.Boilers_Boiler1_Simulation_Resume, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Halt = new ExpandedNodeId(Boiler.Methods.Boilers_Boiler1_Simulation_Halt, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Reset = new ExpandedNodeId(Boiler.Methods.Boilers_Boiler1_Simulation_Reset, Boiler.Namespaces.Boiler);
    }
    #endregion

    #region Object Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId BoilerInputPipeType_FlowTransmitter1 = new ExpandedNodeId(Boiler.Objects.BoilerInputPipeType_FlowTransmitter1, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerInputPipeType_Valve = new ExpandedNodeId(Boiler.Objects.BoilerInputPipeType_Valve, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerDrumType_LevelIndicator = new ExpandedNodeId(Boiler.Objects.BoilerDrumType_LevelIndicator, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerOutputPipeType_FlowTransmitter2 = new ExpandedNodeId(Boiler.Objects.BoilerOutputPipeType_FlowTransmitter2, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_InputPipe = new ExpandedNodeId(Boiler.Objects.BoilerType_InputPipe, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_InputPipe_FlowTransmitter1 = new ExpandedNodeId(Boiler.Objects.BoilerType_InputPipe_FlowTransmitter1, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_InputPipe_Valve = new ExpandedNodeId(Boiler.Objects.BoilerType_InputPipe_Valve, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Drum = new ExpandedNodeId(Boiler.Objects.BoilerType_Drum, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Drum_LevelIndicator = new ExpandedNodeId(Boiler.Objects.BoilerType_Drum_LevelIndicator, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_OutputPipe = new ExpandedNodeId(Boiler.Objects.BoilerType_OutputPipe, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_OutputPipe_FlowTransmitter2 = new ExpandedNodeId(Boiler.Objects.BoilerType_OutputPipe_FlowTransmitter2, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_FlowController = new ExpandedNodeId(Boiler.Objects.BoilerType_FlowController, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_LevelController = new ExpandedNodeId(Boiler.Objects.BoilerType_LevelController, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_CustomController = new ExpandedNodeId(Boiler.Objects.BoilerType_CustomController, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation = new ExpandedNodeId(Boiler.Objects.BoilerType_Simulation, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers = new ExpandedNodeId(Boiler.Objects.Boilers, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1 = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_InputPipe, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_FlowTransmitter1 = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_InputPipe_FlowTransmitter1, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_Valve = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_InputPipe_Valve, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Drum = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_Drum, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Drum_LevelIndicator = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_Drum_LevelIndicator, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_OutputPipe = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_OutputPipe, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_OutputPipe_FlowTransmitter2 = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_OutputPipe_FlowTransmitter2, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_FlowController = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_FlowController, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_LevelController = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_LevelController, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_CustomController, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_Simulation, Boiler.Namespaces.Boiler);
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId GenericControllerType = new ExpandedNodeId(Boiler.ObjectTypes.GenericControllerType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId GenericSensorType = new ExpandedNodeId(Boiler.ObjectTypes.GenericSensorType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId GenericActuatorType = new ExpandedNodeId(Boiler.ObjectTypes.GenericActuatorType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId CustomControllerType = new ExpandedNodeId(Boiler.ObjectTypes.CustomControllerType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId ValveType = new ExpandedNodeId(Boiler.ObjectTypes.ValveType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId LevelControllerType = new ExpandedNodeId(Boiler.ObjectTypes.LevelControllerType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId FlowControllerType = new ExpandedNodeId(Boiler.ObjectTypes.FlowControllerType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId LevelIndicatorType = new ExpandedNodeId(Boiler.ObjectTypes.LevelIndicatorType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId FlowTransmitterType = new ExpandedNodeId(Boiler.ObjectTypes.FlowTransmitterType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType = new ExpandedNodeId(Boiler.ObjectTypes.BoilerStateMachineType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerInputPipeType = new ExpandedNodeId(Boiler.ObjectTypes.BoilerInputPipeType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerDrumType = new ExpandedNodeId(Boiler.ObjectTypes.BoilerDrumType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerOutputPipeType = new ExpandedNodeId(Boiler.ObjectTypes.BoilerOutputPipeType, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType = new ExpandedNodeId(Boiler.ObjectTypes.BoilerType, Boiler.Namespaces.Boiler);
    }
    #endregion

    #region ReferenceType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ReferenceTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId FlowTo = new ExpandedNodeId(Boiler.ReferenceTypes.FlowTo, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId HotFlowTo = new ExpandedNodeId(Boiler.ReferenceTypes.HotFlowTo, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId SignalTo = new ExpandedNodeId(Boiler.ReferenceTypes.SignalTo, Boiler.Namespaces.Boiler);
    }
    #endregion

    #region Variable Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId GenericControllerType_Measurement = new ExpandedNodeId(Boiler.Variables.GenericControllerType_Measurement, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId GenericControllerType_SetPoint = new ExpandedNodeId(Boiler.Variables.GenericControllerType_SetPoint, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId GenericControllerType_ControlOut = new ExpandedNodeId(Boiler.Variables.GenericControllerType_ControlOut, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId GenericSensorType_Output = new ExpandedNodeId(Boiler.Variables.GenericSensorType_Output, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId GenericSensorType_Output_EURange = new ExpandedNodeId(Boiler.Variables.GenericSensorType_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId GenericActuatorType_Input = new ExpandedNodeId(Boiler.Variables.GenericActuatorType_Input, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId GenericActuatorType_Input_EURange = new ExpandedNodeId(Boiler.Variables.GenericActuatorType_Input_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId CustomControllerType_Input1 = new ExpandedNodeId(Boiler.Variables.CustomControllerType_Input1, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId CustomControllerType_Input2 = new ExpandedNodeId(Boiler.Variables.CustomControllerType_Input2, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId CustomControllerType_Input3 = new ExpandedNodeId(Boiler.Variables.CustomControllerType_Input3, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId CustomControllerType_ControlOut = new ExpandedNodeId(Boiler.Variables.CustomControllerType_ControlOut, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId CustomControllerType_DescriptionX = new ExpandedNodeId(Boiler.Variables.CustomControllerType_DescriptionX, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId ValveType_Input_EURange = new ExpandedNodeId(Boiler.Variables.ValveType_Input_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId LevelIndicatorType_Output_EURange = new ExpandedNodeId(Boiler.Variables.LevelIndicatorType_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId FlowTransmitterType_Output_EURange = new ExpandedNodeId(Boiler.Variables.FlowTransmitterType_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_CurrentState_Id = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_CurrentState_Id, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_CurrentState_Number = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_CurrentState_Number, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_LastTransition_Id = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_LastTransition_Id, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_LastTransition_Number = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_LastTransition_Number, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_LastTransition_TransitionTime = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_LastTransition_TransitionTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_CreateSessionId = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_CreateSessionId, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_CreateClientName = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_CreateClientName, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_InvocationCreationTime = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_InvocationCreationTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_LastTransitionTime = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_LastTransitionTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_LastMethodCall = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_LastMethodCall, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_LastMethodSessionId = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_LastMethodSessionId, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_LastMethodInputArguments = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_LastMethodInputArguments, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_LastMethodOutputArguments = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_LastMethodOutputArguments, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_LastMethodInputValues = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_LastMethodInputValues, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_LastMethodOutputValues = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_LastMethodOutputValues, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_LastMethodCallTime = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_LastMethodCallTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ProgramDiagnostic_LastMethodReturnStatus = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ProgramDiagnostic_LastMethodReturnStatus, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_Halted_StateNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_Halted_StateNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_Ready_StateNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_Ready_StateNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_Running_StateNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_Running_StateNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_Suspended_StateNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_Suspended_StateNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_HaltedToReady_TransitionNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_HaltedToReady_TransitionNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ReadyToRunning_TransitionNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ReadyToRunning_TransitionNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_RunningToHalted_TransitionNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_RunningToHalted_TransitionNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_RunningToReady_TransitionNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_RunningToReady_TransitionNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_RunningToSuspended_TransitionNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_RunningToSuspended_TransitionNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_SuspendedToRunning_TransitionNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_SuspendedToRunning_TransitionNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_SuspendedToHalted_TransitionNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_SuspendedToHalted_TransitionNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_SuspendedToReady_TransitionNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_SuspendedToReady_TransitionNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_ReadyToHalted_TransitionNumber = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_ReadyToHalted_TransitionNumber, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerStateMachineType_UpdateRate = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_UpdateRate, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerInputPipeType_FlowTransmitter1_Output = new ExpandedNodeId(Boiler.Variables.BoilerInputPipeType_FlowTransmitter1_Output, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerInputPipeType_FlowTransmitter1_Output_EURange = new ExpandedNodeId(Boiler.Variables.BoilerInputPipeType_FlowTransmitter1_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerInputPipeType_Valve_Input = new ExpandedNodeId(Boiler.Variables.BoilerInputPipeType_Valve_Input, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerInputPipeType_Valve_Input_EURange = new ExpandedNodeId(Boiler.Variables.BoilerInputPipeType_Valve_Input_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerDrumType_LevelIndicator_Output = new ExpandedNodeId(Boiler.Variables.BoilerDrumType_LevelIndicator_Output, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerDrumType_LevelIndicator_Output_EURange = new ExpandedNodeId(Boiler.Variables.BoilerDrumType_LevelIndicator_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerOutputPipeType_FlowTransmitter2_Output = new ExpandedNodeId(Boiler.Variables.BoilerOutputPipeType_FlowTransmitter2_Output, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerOutputPipeType_FlowTransmitter2_Output_EURange = new ExpandedNodeId(Boiler.Variables.BoilerOutputPipeType_FlowTransmitter2_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_InputPipe_FlowTransmitter1_Output = new ExpandedNodeId(Boiler.Variables.BoilerType_InputPipe_FlowTransmitter1_Output, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_InputPipe_FlowTransmitter1_Output_EURange = new ExpandedNodeId(Boiler.Variables.BoilerType_InputPipe_FlowTransmitter1_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_InputPipe_Valve_Input = new ExpandedNodeId(Boiler.Variables.BoilerType_InputPipe_Valve_Input, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_InputPipe_Valve_Input_EURange = new ExpandedNodeId(Boiler.Variables.BoilerType_InputPipe_Valve_Input_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Drum_LevelIndicator_Output = new ExpandedNodeId(Boiler.Variables.BoilerType_Drum_LevelIndicator_Output, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Drum_LevelIndicator_Output_EURange = new ExpandedNodeId(Boiler.Variables.BoilerType_Drum_LevelIndicator_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_OutputPipe_FlowTransmitter2_Output = new ExpandedNodeId(Boiler.Variables.BoilerType_OutputPipe_FlowTransmitter2_Output, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_OutputPipe_FlowTransmitter2_Output_EURange = new ExpandedNodeId(Boiler.Variables.BoilerType_OutputPipe_FlowTransmitter2_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_FlowController_Measurement = new ExpandedNodeId(Boiler.Variables.BoilerType_FlowController_Measurement, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_FlowController_SetPoint = new ExpandedNodeId(Boiler.Variables.BoilerType_FlowController_SetPoint, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_FlowController_ControlOut = new ExpandedNodeId(Boiler.Variables.BoilerType_FlowController_ControlOut, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_LevelController_Measurement = new ExpandedNodeId(Boiler.Variables.BoilerType_LevelController_Measurement, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_LevelController_SetPoint = new ExpandedNodeId(Boiler.Variables.BoilerType_LevelController_SetPoint, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_LevelController_ControlOut = new ExpandedNodeId(Boiler.Variables.BoilerType_LevelController_ControlOut, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_CustomController_Input1 = new ExpandedNodeId(Boiler.Variables.BoilerType_CustomController_Input1, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_CustomController_Input2 = new ExpandedNodeId(Boiler.Variables.BoilerType_CustomController_Input2, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_CustomController_Input3 = new ExpandedNodeId(Boiler.Variables.BoilerType_CustomController_Input3, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_CustomController_ControlOut = new ExpandedNodeId(Boiler.Variables.BoilerType_CustomController_ControlOut, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_CustomController_DescriptionX = new ExpandedNodeId(Boiler.Variables.BoilerType_CustomController_DescriptionX, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_CurrentState = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_CurrentState, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_CurrentState_Id = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_CurrentState_Id, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_CurrentState_Number = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_CurrentState_Number, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_LastTransition = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_LastTransition, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_LastTransition_Id = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_LastTransition_Id, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_LastTransition_Number = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_LastTransition_Number, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_LastTransition_TransitionTime = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_LastTransition_TransitionTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_Deletable = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_Deletable, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_AutoDelete = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_AutoDelete, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_RecycleCount = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_RecycleCount, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_CreateSessionId = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_CreateSessionId, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_CreateClientName = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_CreateClientName, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_InvocationCreationTime = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_InvocationCreationTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_LastTransitionTime = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_LastTransitionTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_LastMethodCall = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_LastMethodCall, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_LastMethodSessionId = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_LastMethodSessionId, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_LastMethodInputArguments = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_LastMethodInputArguments, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_LastMethodOutputArguments = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_LastMethodOutputArguments, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_LastMethodInputValues = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_LastMethodInputValues, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_LastMethodOutputValues = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_LastMethodOutputValues, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_LastMethodCallTime = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_LastMethodCallTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_ProgramDiagnostic_LastMethodReturnStatus = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_ProgramDiagnostic_LastMethodReturnStatus, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId BoilerType_Simulation_UpdateRate = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_UpdateRate, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_FlowTransmitter1_Output = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_InputPipe_FlowTransmitter1_Output, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_FlowTransmitter1_Output_EURange = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_InputPipe_FlowTransmitter1_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_Valve_Input = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_InputPipe_Valve_Input, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_Valve_Input_EURange = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_InputPipe_Valve_Input_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Drum_LevelIndicator_Output = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Drum_LevelIndicator_Output, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Drum_LevelIndicator_Output_EURange = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Drum_LevelIndicator_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output_EURange = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output_EURange, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_FlowController_Measurement = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_FlowController_Measurement, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_FlowController_SetPoint = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_FlowController_SetPoint, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_FlowController_ControlOut = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_FlowController_ControlOut, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_LevelController_Measurement = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_LevelController_Measurement, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_LevelController_SetPoint = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_LevelController_SetPoint, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_LevelController_ControlOut = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_LevelController_ControlOut, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController_Input1 = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_CustomController_Input1, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController_Input2 = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_CustomController_Input2, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController_Input3 = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_CustomController_Input3, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController_ControlOut = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_CustomController_ControlOut, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController_DescriptionX = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_CustomController_DescriptionX, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_CurrentState = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_CurrentState, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_CurrentState_Id = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_CurrentState_Id, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_CurrentState_Number = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_CurrentState_Number, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_LastTransition = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_LastTransition, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_LastTransition_Id = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_LastTransition_Id, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_LastTransition_Number = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_LastTransition_Number, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_LastTransition_TransitionTime = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_LastTransition_TransitionTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Deletable = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_Deletable, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_AutoDelete = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_AutoDelete, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_RecycleCount = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_RecycleCount, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_CreateSessionId = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_CreateSessionId, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_CreateClientName = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_CreateClientName, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_InvocationCreationTime = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_InvocationCreationTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_LastTransitionTime = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_LastTransitionTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodCall = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodCall, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodSessionId = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodSessionId, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodInputArguments = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodInputArguments, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodOutputArguments = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodOutputArguments, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodInputValues = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodInputValues, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodOutputValues = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodOutputValues, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodCallTime = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodCallTime, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodReturnStatus = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodReturnStatus, Boiler.Namespaces.Boiler);

        /// <remarks />
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_UpdateRate = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_UpdateRate, Boiler.Namespaces.Boiler);
    }
    #endregion

    #region BrowseName Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class BrowseNames
    {
        /// <remarks />
        public const string Boiler1 = "Boiler #1";

        /// <remarks />
        public const string BoilerDrumType = "BoilerDrumType";

        /// <remarks />
        public const string BoilerInputPipeType = "BoilerInputPipeType";

        /// <remarks />
        public const string BoilerOutputPipeType = "BoilerOutputPipeType";

        /// <remarks />
        public const string Boilers = "Boilers";

        /// <remarks />
        public const string BoilerStateMachineType = "BoilerStateMachineType";

        /// <remarks />
        public const string BoilerType = "BoilerType";

        /// <remarks />
        public const string ControlOut = "ControlOut";

        /// <remarks />
        public const string CustomController = "CCX001";

        /// <remarks />
        public const string CustomControllerType = "CustomControllerType";

        /// <remarks />
        public const string DescriptionX = "Description";

        /// <remarks />
        public const string Drum = "DrumX001";

        /// <remarks />
        public const string FlowController = "FCX001";

        /// <remarks />
        public const string FlowControllerType = "FlowControllerType";

        /// <remarks />
        public const string FlowTo = "FlowTo";

        /// <remarks />
        public const string FlowTransmitter1 = "FTX001";

        /// <remarks />
        public const string FlowTransmitter2 = "FTX002";

        /// <remarks />
        public const string FlowTransmitterType = "FlowTransmitterType";

        /// <remarks />
        public const string GenericActuatorType = "GenericActuatorType";

        /// <remarks />
        public const string GenericControllerType = "GenericControllerType";

        /// <remarks />
        public const string GenericSensorType = "GenericSensorType";

        /// <remarks />
        public const string Halt = "Halt";

        /// <remarks />
        public const string HotFlowTo = "HotFlowTo";

        /// <remarks />
        public const string Input = "Input";

        /// <remarks />
        public const string Input1 = "Input1";

        /// <remarks />
        public const string Input2 = "Input2";

        /// <remarks />
        public const string Input3 = "Input3";

        /// <remarks />
        public const string InputPipe = "PipeX001";

        /// <remarks />
        public const string LevelController = "LCX001";

        /// <remarks />
        public const string LevelControllerType = "LevelControllerType";

        /// <remarks />
        public const string LevelIndicator = "LIX001";

        /// <remarks />
        public const string LevelIndicatorType = "LevelIndicatorType";

        /// <remarks />
        public const string Measurement = "Measurement";

        /// <remarks />
        public const string Output = "Output";

        /// <remarks />
        public const string OutputPipe = "PipeX002";

        /// <remarks />
        public const string Reset = "Reset";

        /// <remarks />
        public const string Resume = "Resume";

        /// <remarks />
        public const string SetPoint = "SetPoint";

        /// <remarks />
        public const string SignalTo = "SignalTo";

        /// <remarks />
        public const string Simulation = "Simulation";

        /// <remarks />
        public const string Start = "Start";

        /// <remarks />
        public const string Suspend = "Suspend";

        /// <remarks />
        public const string UpdateRate = "UpdateRate";

        /// <remarks />
        public const string Valve = "ValveX001";

        /// <remarks />
        public const string ValveType = "ValveType";
    }
    #endregion

    #region Namespace Declarations
    /// <remarks />
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
        /// The URI for the Boiler namespace (.NET code namespace is 'Boiler').
        /// </summary>
        public const string Boiler = "http://opcfoundation.org/UA/Boiler/";
    }
    #endregion
}