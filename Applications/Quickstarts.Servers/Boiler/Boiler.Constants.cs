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
        public const uint BoilerStateMachineType_Start = 1118;

        /// <remarks />
        public const uint BoilerStateMachineType_Suspend = 1119;

        /// <remarks />
        public const uint BoilerStateMachineType_Resume = 1120;

        /// <remarks />
        public const uint BoilerStateMachineType_Halt = 1121;

        /// <remarks />
        public const uint BoilerStateMachineType_Reset = 1122;

        /// <remarks />
        public const uint BoilerType_Simulation_Start = 1233;

        /// <remarks />
        public const uint BoilerType_Simulation_Suspend = 1234;

        /// <remarks />
        public const uint BoilerType_Simulation_Resume = 1235;

        /// <remarks />
        public const uint BoilerType_Simulation_Halt = 1236;

        /// <remarks />
        public const uint BoilerType_Simulation_Reset = 1237;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Start = 1317;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Suspend = 1318;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Resume = 1319;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Halt = 1320;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Reset = 1321;
    }
    #endregion

    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint BoilerInputPipeType_FlowTransmitter1 = 1125;

        /// <remarks />
        public const uint BoilerInputPipeType_Valve = 1132;

        /// <remarks />
        public const uint BoilerDrumType_LevelIndicator = 1140;

        /// <remarks />
        public const uint BoilerOutputPipeType_FlowTransmitter2 = 1148;

        /// <remarks />
        public const uint BoilerType_InputPipe = 1156;

        /// <remarks />
        public const uint BoilerType_InputPipe_FlowTransmitter1 = 1157;

        /// <remarks />
        public const uint BoilerType_InputPipe_Valve = 1164;

        /// <remarks />
        public const uint BoilerType_Drum = 1171;

        /// <remarks />
        public const uint BoilerType_Drum_LevelIndicator = 1172;

        /// <remarks />
        public const uint BoilerType_OutputPipe = 1179;

        /// <remarks />
        public const uint BoilerType_OutputPipe_FlowTransmitter2 = 1180;

        /// <remarks />
        public const uint BoilerType_FlowController = 1187;

        /// <remarks />
        public const uint BoilerType_LevelController = 1191;

        /// <remarks />
        public const uint BoilerType_CustomController = 1195;

        /// <remarks />
        public const uint BoilerType_Simulation = 1201;

        /// <remarks />
        public const uint Boilers = 1238;

        /// <remarks />
        public const uint Boilers_Boiler1 = 1239;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe = 1240;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_FlowTransmitter1 = 1241;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_Valve = 1248;

        /// <remarks />
        public const uint Boilers_Boiler1_Drum = 1255;

        /// <remarks />
        public const uint Boilers_Boiler1_Drum_LevelIndicator = 1256;

        /// <remarks />
        public const uint Boilers_Boiler1_OutputPipe = 1263;

        /// <remarks />
        public const uint Boilers_Boiler1_OutputPipe_FlowTransmitter2 = 1264;

        /// <remarks />
        public const uint Boilers_Boiler1_FlowController = 1271;

        /// <remarks />
        public const uint Boilers_Boiler1_LevelController = 1275;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController = 1279;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation = 1285;
    }
    #endregion

    #region ObjectType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <remarks />
        public const uint GenericControllerType = 1004;

        /// <remarks />
        public const uint GenericSensorType = 1008;

        /// <remarks />
        public const uint GenericActuatorType = 1015;

        /// <remarks />
        public const uint CustomControllerType = 1022;

        /// <remarks />
        public const uint ValveType = 1028;

        /// <remarks />
        public const uint LevelControllerType = 1035;

        /// <remarks />
        public const uint FlowControllerType = 1039;

        /// <remarks />
        public const uint LevelIndicatorType = 1043;

        /// <remarks />
        public const uint FlowTransmitterType = 1050;

        /// <remarks />
        public const uint BoilerStateMachineType = 1057;

        /// <remarks />
        public const uint BoilerInputPipeType = 1124;

        /// <remarks />
        public const uint BoilerDrumType = 1139;

        /// <remarks />
        public const uint BoilerOutputPipeType = 1147;

        /// <remarks />
        public const uint BoilerType = 1155;
    }
    #endregion

    #region ReferenceType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ReferenceTypes
    {
        /// <remarks />
        public const uint FlowTo = 1001;

        /// <remarks />
        public const uint HotFlowTo = 1002;

        /// <remarks />
        public const uint SignalTo = 1003;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint GenericControllerType_Measurement = 1005;

        /// <remarks />
        public const uint GenericControllerType_SetPoint = 1006;

        /// <remarks />
        public const uint GenericControllerType_ControlOut = 1007;

        /// <remarks />
        public const uint GenericSensorType_Output = 1009;

        /// <remarks />
        public const uint GenericSensorType_Output_EURange = 1013;

        /// <remarks />
        public const uint GenericActuatorType_Input = 1016;

        /// <remarks />
        public const uint GenericActuatorType_Input_EURange = 1020;

        /// <remarks />
        public const uint CustomControllerType_Input1 = 1023;

        /// <remarks />
        public const uint CustomControllerType_Input2 = 1024;

        /// <remarks />
        public const uint CustomControllerType_Input3 = 1025;

        /// <remarks />
        public const uint CustomControllerType_ControlOut = 1026;

        /// <remarks />
        public const uint CustomControllerType_DescriptionX = 1027;

        /// <remarks />
        public const uint ValveType_Input_EURange = 1033;

        /// <remarks />
        public const uint LevelIndicatorType_Output_EURange = 1048;

        /// <remarks />
        public const uint FlowTransmitterType_Output_EURange = 1055;

        /// <remarks />
        public const uint BoilerStateMachineType_CurrentState_Id = 1059;

        /// <remarks />
        public const uint BoilerStateMachineType_CurrentState_Number = 1061;

        /// <remarks />
        public const uint BoilerStateMachineType_LastTransition_Id = 1064;

        /// <remarks />
        public const uint BoilerStateMachineType_LastTransition_Number = 1066;

        /// <remarks />
        public const uint BoilerStateMachineType_LastTransition_TransitionTime = 1067;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_CreateSessionId = 1079;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_CreateClientName = 1080;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_InvocationCreationTime = 1081;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastTransitionTime = 1082;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodCall = 1083;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodSessionId = 1084;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodInputArguments = 1085;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodOutputArguments = 1086;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodInputValues = 1087;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodOutputValues = 1088;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodCallTime = 1089;

        /// <remarks />
        public const uint BoilerStateMachineType_ProgramDiagnostic_LastMethodReturnStatus = 1090;

        /// <remarks />
        public const uint BoilerStateMachineType_Halted_StateNumber = 1093;

        /// <remarks />
        public const uint BoilerStateMachineType_Ready_StateNumber = 1095;

        /// <remarks />
        public const uint BoilerStateMachineType_Running_StateNumber = 1097;

        /// <remarks />
        public const uint BoilerStateMachineType_Suspended_StateNumber = 1099;

        /// <remarks />
        public const uint BoilerStateMachineType_HaltedToReady_TransitionNumber = 1101;

        /// <remarks />
        public const uint BoilerStateMachineType_ReadyToRunning_TransitionNumber = 1103;

        /// <remarks />
        public const uint BoilerStateMachineType_RunningToHalted_TransitionNumber = 1105;

        /// <remarks />
        public const uint BoilerStateMachineType_RunningToReady_TransitionNumber = 1107;

        /// <remarks />
        public const uint BoilerStateMachineType_RunningToSuspended_TransitionNumber = 1109;

        /// <remarks />
        public const uint BoilerStateMachineType_SuspendedToRunning_TransitionNumber = 1111;

        /// <remarks />
        public const uint BoilerStateMachineType_SuspendedToHalted_TransitionNumber = 1113;

        /// <remarks />
        public const uint BoilerStateMachineType_SuspendedToReady_TransitionNumber = 1115;

        /// <remarks />
        public const uint BoilerStateMachineType_ReadyToHalted_TransitionNumber = 1117;

        /// <remarks />
        public const uint BoilerStateMachineType_UpdateRate = 1123;

        /// <remarks />
        public const uint BoilerInputPipeType_FlowTransmitter1_Output = 1126;

        /// <remarks />
        public const uint BoilerInputPipeType_FlowTransmitter1_Output_EURange = 1130;

        /// <remarks />
        public const uint BoilerInputPipeType_Valve_Input = 1133;

        /// <remarks />
        public const uint BoilerInputPipeType_Valve_Input_EURange = 1137;

        /// <remarks />
        public const uint BoilerDrumType_LevelIndicator_Output = 1141;

        /// <remarks />
        public const uint BoilerDrumType_LevelIndicator_Output_EURange = 1145;

        /// <remarks />
        public const uint BoilerOutputPipeType_FlowTransmitter2_Output = 1149;

        /// <remarks />
        public const uint BoilerOutputPipeType_FlowTransmitter2_Output_EURange = 1153;

        /// <remarks />
        public const uint BoilerType_InputPipe_FlowTransmitter1_Output = 1158;

        /// <remarks />
        public const uint BoilerType_InputPipe_FlowTransmitter1_Output_EURange = 1162;

        /// <remarks />
        public const uint BoilerType_InputPipe_Valve_Input = 1165;

        /// <remarks />
        public const uint BoilerType_InputPipe_Valve_Input_EURange = 1169;

        /// <remarks />
        public const uint BoilerType_Drum_LevelIndicator_Output = 1173;

        /// <remarks />
        public const uint BoilerType_Drum_LevelIndicator_Output_EURange = 1177;

        /// <remarks />
        public const uint BoilerType_OutputPipe_FlowTransmitter2_Output = 1181;

        /// <remarks />
        public const uint BoilerType_OutputPipe_FlowTransmitter2_Output_EURange = 1185;

        /// <remarks />
        public const uint BoilerType_FlowController_Measurement = 1188;

        /// <remarks />
        public const uint BoilerType_FlowController_SetPoint = 1189;

        /// <remarks />
        public const uint BoilerType_FlowController_ControlOut = 1190;

        /// <remarks />
        public const uint BoilerType_LevelController_Measurement = 1192;

        /// <remarks />
        public const uint BoilerType_LevelController_SetPoint = 1193;

        /// <remarks />
        public const uint BoilerType_LevelController_ControlOut = 1194;

        /// <remarks />
        public const uint BoilerType_CustomController_Input1 = 1196;

        /// <remarks />
        public const uint BoilerType_CustomController_Input2 = 1197;

        /// <remarks />
        public const uint BoilerType_CustomController_Input3 = 1198;

        /// <remarks />
        public const uint BoilerType_CustomController_ControlOut = 1199;

        /// <remarks />
        public const uint BoilerType_CustomController_DescriptionX = 1200;

        /// <remarks />
        public const uint BoilerType_Simulation_CurrentState = 1202;

        /// <remarks />
        public const uint BoilerType_Simulation_CurrentState_Id = 1203;

        /// <remarks />
        public const uint BoilerType_Simulation_CurrentState_Number = 1205;

        /// <remarks />
        public const uint BoilerType_Simulation_LastTransition = 1207;

        /// <remarks />
        public const uint BoilerType_Simulation_LastTransition_Id = 1208;

        /// <remarks />
        public const uint BoilerType_Simulation_LastTransition_Number = 1210;

        /// <remarks />
        public const uint BoilerType_Simulation_LastTransition_TransitionTime = 1211;

        /// <remarks />
        public const uint BoilerType_Simulation_Deletable = 1215;

        /// <remarks />
        public const uint BoilerType_Simulation_AutoDelete = 1216;

        /// <remarks />
        public const uint BoilerType_Simulation_RecycleCount = 1217;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_CreateSessionId = 1219;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_CreateClientName = 1220;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_InvocationCreationTime = 1221;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastTransitionTime = 1222;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodCall = 1223;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodSessionId = 1224;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodInputArguments = 1225;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodOutputArguments = 1226;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodInputValues = 1227;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodOutputValues = 1228;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodCallTime = 1229;

        /// <remarks />
        public const uint BoilerType_Simulation_ProgramDiagnostic_LastMethodReturnStatus = 1230;

        /// <remarks />
        public const uint BoilerType_Simulation_UpdateRate = 1232;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_FlowTransmitter1_Output = 1242;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_FlowTransmitter1_Output_EURange = 1246;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_Valve_Input = 1249;

        /// <remarks />
        public const uint Boilers_Boiler1_InputPipe_Valve_Input_EURange = 1253;

        /// <remarks />
        public const uint Boilers_Boiler1_Drum_LevelIndicator_Output = 1257;

        /// <remarks />
        public const uint Boilers_Boiler1_Drum_LevelIndicator_Output_EURange = 1261;

        /// <remarks />
        public const uint Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output = 1265;

        /// <remarks />
        public const uint Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output_EURange = 1269;

        /// <remarks />
        public const uint Boilers_Boiler1_FlowController_Measurement = 1272;

        /// <remarks />
        public const uint Boilers_Boiler1_FlowController_SetPoint = 1273;

        /// <remarks />
        public const uint Boilers_Boiler1_FlowController_ControlOut = 1274;

        /// <remarks />
        public const uint Boilers_Boiler1_LevelController_Measurement = 1276;

        /// <remarks />
        public const uint Boilers_Boiler1_LevelController_SetPoint = 1277;

        /// <remarks />
        public const uint Boilers_Boiler1_LevelController_ControlOut = 1278;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController_Input1 = 1280;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController_Input2 = 1281;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController_Input3 = 1282;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController_ControlOut = 1283;

        /// <remarks />
        public const uint Boilers_Boiler1_CustomController_DescriptionX = 1284;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_CurrentState = 1286;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_CurrentState_Id = 1287;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_CurrentState_Number = 1289;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_LastTransition = 1291;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_LastTransition_Id = 1292;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_LastTransition_Number = 1294;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_LastTransition_TransitionTime = 1295;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_Deletable = 1299;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_AutoDelete = 1300;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_RecycleCount = 1301;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_CreateSessionId = 1303;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_CreateClientName = 1304;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_InvocationCreationTime = 1305;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastTransitionTime = 1306;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodCall = 1307;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodSessionId = 1308;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodInputArguments = 1309;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodOutputArguments = 1310;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodInputValues = 1311;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodOutputValues = 1312;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodCallTime = 1313;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostic_LastMethodReturnStatus = 1314;

        /// <remarks />
        public const uint Boilers_Boiler1_Simulation_UpdateRate = 1316;
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