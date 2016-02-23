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

namespace Boiler
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
        /// The identifier for the Boilers_Boiler1_Simulation_Start Method.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_Start = 1343;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_Suspend Method.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_Suspend = 1344;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_Resume Method.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_Resume = 1345;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_Halt Method.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_Halt = 1346;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_Reset Method.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_Reset = 1347;
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
        /// The identifier for the BoilerInputPipeType_FlowTransmitter1 Object.
        /// </summary>
        public const uint BoilerInputPipeType_FlowTransmitter1 = 1102;

        /// <summary>
        /// The identifier for the BoilerInputPipeType_Valve Object.
        /// </summary>
        public const uint BoilerInputPipeType_Valve = 1109;

        /// <summary>
        /// The identifier for the BoilerDrumType_LevelIndicator Object.
        /// </summary>
        public const uint BoilerDrumType_LevelIndicator = 1117;

        /// <summary>
        /// The identifier for the BoilerOutputPipeType_FlowTransmitter2 Object.
        /// </summary>
        public const uint BoilerOutputPipeType_FlowTransmitter2 = 1125;

        /// <summary>
        /// The identifier for the BoilerType_InputPipe Object.
        /// </summary>
        public const uint BoilerType_InputPipe = 1133;

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_FlowTransmitter1 Object.
        /// </summary>
        public const uint BoilerType_InputPipe_FlowTransmitter1 = 1134;

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_Valve Object.
        /// </summary>
        public const uint BoilerType_InputPipe_Valve = 1141;

        /// <summary>
        /// The identifier for the BoilerType_Drum Object.
        /// </summary>
        public const uint BoilerType_Drum = 1148;

        /// <summary>
        /// The identifier for the BoilerType_Drum_LevelIndicator Object.
        /// </summary>
        public const uint BoilerType_Drum_LevelIndicator = 1149;

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe Object.
        /// </summary>
        public const uint BoilerType_OutputPipe = 1156;

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe_FlowTransmitter2 Object.
        /// </summary>
        public const uint BoilerType_OutputPipe_FlowTransmitter2 = 1157;

        /// <summary>
        /// The identifier for the BoilerType_FlowController Object.
        /// </summary>
        public const uint BoilerType_FlowController = 1164;

        /// <summary>
        /// The identifier for the BoilerType_LevelController Object.
        /// </summary>
        public const uint BoilerType_LevelController = 1168;

        /// <summary>
        /// The identifier for the BoilerType_CustomController Object.
        /// </summary>
        public const uint BoilerType_CustomController = 1172;

        /// <summary>
        /// The identifier for the BoilerType_Simulation Object.
        /// </summary>
        public const uint BoilerType_Simulation = 1178;

        /// <summary>
        /// The identifier for the Boilers Object.
        /// </summary>
        public const uint Boilers = 1240;

        /// <summary>
        /// The identifier for the Boilers_Boiler1 Object.
        /// </summary>
        public const uint Boilers_Boiler1 = 1241;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe Object.
        /// </summary>
        public const uint Boilers_Boiler1_InputPipe = 1242;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_FlowTransmitter1 Object.
        /// </summary>
        public const uint Boilers_Boiler1_InputPipe_FlowTransmitter1 = 1243;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_Valve Object.
        /// </summary>
        public const uint Boilers_Boiler1_InputPipe_Valve = 1250;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Drum Object.
        /// </summary>
        public const uint Boilers_Boiler1_Drum = 1257;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Drum_LevelIndicator Object.
        /// </summary>
        public const uint Boilers_Boiler1_Drum_LevelIndicator = 1258;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_OutputPipe Object.
        /// </summary>
        public const uint Boilers_Boiler1_OutputPipe = 1265;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_OutputPipe_FlowTransmitter2 Object.
        /// </summary>
        public const uint Boilers_Boiler1_OutputPipe_FlowTransmitter2 = 1266;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_FlowController Object.
        /// </summary>
        public const uint Boilers_Boiler1_FlowController = 1273;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_LevelController Object.
        /// </summary>
        public const uint Boilers_Boiler1_LevelController = 1277;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController Object.
        /// </summary>
        public const uint Boilers_Boiler1_CustomController = 1281;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation Object.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation = 1287;
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
        /// The identifier for the GenericControllerType ObjectType.
        /// </summary>
        public const uint GenericControllerType = 210;

        /// <summary>
        /// The identifier for the GenericSensorType ObjectType.
        /// </summary>
        public const uint GenericSensorType = 991;

        /// <summary>
        /// The identifier for the GenericActuatorType ObjectType.
        /// </summary>
        public const uint GenericActuatorType = 998;

        /// <summary>
        /// The identifier for the CustomControllerType ObjectType.
        /// </summary>
        public const uint CustomControllerType = 513;

        /// <summary>
        /// The identifier for the ValveType ObjectType.
        /// </summary>
        public const uint ValveType = 1010;

        /// <summary>
        /// The identifier for the LevelControllerType ObjectType.
        /// </summary>
        public const uint LevelControllerType = 1017;

        /// <summary>
        /// The identifier for the FlowControllerType ObjectType.
        /// </summary>
        public const uint FlowControllerType = 1021;

        /// <summary>
        /// The identifier for the LevelIndicatorType ObjectType.
        /// </summary>
        public const uint LevelIndicatorType = 1025;

        /// <summary>
        /// The identifier for the FlowTransmitterType ObjectType.
        /// </summary>
        public const uint FlowTransmitterType = 1032;

        /// <summary>
        /// The identifier for the BoilerStateMachineType ObjectType.
        /// </summary>
        public const uint BoilerStateMachineType = 1039;

        /// <summary>
        /// The identifier for the BoilerInputPipeType ObjectType.
        /// </summary>
        public const uint BoilerInputPipeType = 1101;

        /// <summary>
        /// The identifier for the BoilerDrumType ObjectType.
        /// </summary>
        public const uint BoilerDrumType = 1116;

        /// <summary>
        /// The identifier for the BoilerOutputPipeType ObjectType.
        /// </summary>
        public const uint BoilerOutputPipeType = 1124;

        /// <summary>
        /// The identifier for the BoilerType ObjectType.
        /// </summary>
        public const uint BoilerType = 1132;
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
        /// <summary>
        /// The identifier for the FlowTo ReferenceType.
        /// </summary>
        public const uint FlowTo = 985;

        /// <summary>
        /// The identifier for the HotFlowTo ReferenceType.
        /// </summary>
        public const uint HotFlowTo = 986;

        /// <summary>
        /// The identifier for the SignalTo ReferenceType.
        /// </summary>
        public const uint SignalTo = 987;
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
        /// The identifier for the GenericControllerType_Measurement Variable.
        /// </summary>
        public const uint GenericControllerType_Measurement = 988;

        /// <summary>
        /// The identifier for the GenericControllerType_SetPoint Variable.
        /// </summary>
        public const uint GenericControllerType_SetPoint = 989;

        /// <summary>
        /// The identifier for the GenericControllerType_ControlOut Variable.
        /// </summary>
        public const uint GenericControllerType_ControlOut = 990;

        /// <summary>
        /// The identifier for the GenericSensorType_Output Variable.
        /// </summary>
        public const uint GenericSensorType_Output = 992;

        /// <summary>
        /// The identifier for the GenericSensorType_Output_EURange Variable.
        /// </summary>
        public const uint GenericSensorType_Output_EURange = 995;

        /// <summary>
        /// The identifier for the GenericActuatorType_Input Variable.
        /// </summary>
        public const uint GenericActuatorType_Input = 999;

        /// <summary>
        /// The identifier for the GenericActuatorType_Input_EURange Variable.
        /// </summary>
        public const uint GenericActuatorType_Input_EURange = 1002;

        /// <summary>
        /// The identifier for the CustomControllerType_Input1 Variable.
        /// </summary>
        public const uint CustomControllerType_Input1 = 1005;

        /// <summary>
        /// The identifier for the CustomControllerType_Input2 Variable.
        /// </summary>
        public const uint CustomControllerType_Input2 = 1006;

        /// <summary>
        /// The identifier for the CustomControllerType_Input3 Variable.
        /// </summary>
        public const uint CustomControllerType_Input3 = 1007;

        /// <summary>
        /// The identifier for the CustomControllerType_ControlOut Variable.
        /// </summary>
        public const uint CustomControllerType_ControlOut = 1008;

        /// <summary>
        /// The identifier for the CustomControllerType_DescriptionX Variable.
        /// </summary>
        public const uint CustomControllerType_DescriptionX = 1009;

        /// <summary>
        /// The identifier for the BoilerStateMachineType_UpdateRate Variable.
        /// </summary>
        public const uint BoilerStateMachineType_UpdateRate = 1100;

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_FlowTransmitter1_Output Variable.
        /// </summary>
        public const uint BoilerType_InputPipe_FlowTransmitter1_Output = 1135;

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_Valve_Input Variable.
        /// </summary>
        public const uint BoilerType_InputPipe_Valve_Input = 1142;

        /// <summary>
        /// The identifier for the BoilerType_Drum_LevelIndicator_Output Variable.
        /// </summary>
        public const uint BoilerType_Drum_LevelIndicator_Output = 1150;

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe_FlowTransmitter2_Output Variable.
        /// </summary>
        public const uint BoilerType_OutputPipe_FlowTransmitter2_Output = 1158;

        /// <summary>
        /// The identifier for the BoilerType_FlowController_Measurement Variable.
        /// </summary>
        public const uint BoilerType_FlowController_Measurement = 1165;

        /// <summary>
        /// The identifier for the BoilerType_FlowController_SetPoint Variable.
        /// </summary>
        public const uint BoilerType_FlowController_SetPoint = 1166;

        /// <summary>
        /// The identifier for the BoilerType_FlowController_ControlOut Variable.
        /// </summary>
        public const uint BoilerType_FlowController_ControlOut = 1167;

        /// <summary>
        /// The identifier for the BoilerType_LevelController_Measurement Variable.
        /// </summary>
        public const uint BoilerType_LevelController_Measurement = 1169;

        /// <summary>
        /// The identifier for the BoilerType_LevelController_SetPoint Variable.
        /// </summary>
        public const uint BoilerType_LevelController_SetPoint = 1170;

        /// <summary>
        /// The identifier for the BoilerType_LevelController_ControlOut Variable.
        /// </summary>
        public const uint BoilerType_LevelController_ControlOut = 1171;

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input1 Variable.
        /// </summary>
        public const uint BoilerType_CustomController_Input1 = 1173;

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input2 Variable.
        /// </summary>
        public const uint BoilerType_CustomController_Input2 = 1174;

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input3 Variable.
        /// </summary>
        public const uint BoilerType_CustomController_Input3 = 1175;

        /// <summary>
        /// The identifier for the BoilerType_CustomController_ControlOut Variable.
        /// </summary>
        public const uint BoilerType_CustomController_ControlOut = 1176;

        /// <summary>
        /// The identifier for the BoilerType_CustomController_DescriptionX Variable.
        /// </summary>
        public const uint BoilerType_CustomController_DescriptionX = 1177;

        /// <summary>
        /// The identifier for the BoilerType_Simulation_UpdateRate Variable.
        /// </summary>
        public const uint BoilerType_Simulation_UpdateRate = 1239;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_FlowTransmitter1_Output Variable.
        /// </summary>
        public const uint Boilers_Boiler1_InputPipe_FlowTransmitter1_Output = 1244;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_FlowTransmitter1_Output_EURange Variable.
        /// </summary>
        public const uint Boilers_Boiler1_InputPipe_FlowTransmitter1_Output_EURange = 1247;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_Valve_Input Variable.
        /// </summary>
        public const uint Boilers_Boiler1_InputPipe_Valve_Input = 1251;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_Valve_Input_EURange Variable.
        /// </summary>
        public const uint Boilers_Boiler1_InputPipe_Valve_Input_EURange = 1254;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Drum_LevelIndicator_Output Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Drum_LevelIndicator_Output = 1259;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Drum_LevelIndicator_Output_EURange Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Drum_LevelIndicator_Output_EURange = 1262;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output Variable.
        /// </summary>
        public const uint Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output = 1267;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output_EURange Variable.
        /// </summary>
        public const uint Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output_EURange = 1270;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_FlowController_Measurement Variable.
        /// </summary>
        public const uint Boilers_Boiler1_FlowController_Measurement = 1274;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_FlowController_SetPoint Variable.
        /// </summary>
        public const uint Boilers_Boiler1_FlowController_SetPoint = 1275;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_FlowController_ControlOut Variable.
        /// </summary>
        public const uint Boilers_Boiler1_FlowController_ControlOut = 1276;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_LevelController_Measurement Variable.
        /// </summary>
        public const uint Boilers_Boiler1_LevelController_Measurement = 1278;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_LevelController_SetPoint Variable.
        /// </summary>
        public const uint Boilers_Boiler1_LevelController_SetPoint = 1279;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_LevelController_ControlOut Variable.
        /// </summary>
        public const uint Boilers_Boiler1_LevelController_ControlOut = 1280;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController_Input1 Variable.
        /// </summary>
        public const uint Boilers_Boiler1_CustomController_Input1 = 1282;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController_Input2 Variable.
        /// </summary>
        public const uint Boilers_Boiler1_CustomController_Input2 = 1283;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController_Input3 Variable.
        /// </summary>
        public const uint Boilers_Boiler1_CustomController_Input3 = 1284;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController_ControlOut Variable.
        /// </summary>
        public const uint Boilers_Boiler1_CustomController_ControlOut = 1285;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController_DescriptionX Variable.
        /// </summary>
        public const uint Boilers_Boiler1_CustomController_DescriptionX = 1286;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_CurrentState Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_CurrentState = 1288;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_CurrentState_Id Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_CurrentState_Id = 1289;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_CurrentState_Number Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_CurrentState_Number = 1291;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_LastTransition Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_LastTransition = 1293;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_LastTransition_Id Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_LastTransition_Id = 1294;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_LastTransition_Number Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_LastTransition_Number = 1296;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_LastTransition_TransitionTime Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_LastTransition_TransitionTime = 1297;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_Deletable Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_Deletable = 1299;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_RecycleCount Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_RecycleCount = 1301;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_CreateSessionId Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostics_CreateSessionId = 1306;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_CreateClientName Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostics_CreateClientName = 1307;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_InvocationCreationTime Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostics_InvocationCreationTime = 1308;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastTransitionTime Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostics_LastTransitionTime = 1309;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodCall Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodCall = 1310;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodSessionId Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodSessionId = 1311;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodInputArguments Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodInputArguments = 1312;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodOutputArguments Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodOutputArguments = 1313;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodCallTime Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodCallTime = 1314;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodReturnStatus Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodReturnStatus = 1315;

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_UpdateRate Variable.
        /// </summary>
        public const uint Boilers_Boiler1_Simulation_UpdateRate = 1348;
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
        /// The identifier for the Boilers_Boiler1_Simulation_Start Method.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Start = new ExpandedNodeId(Boiler.Methods.Boilers_Boiler1_Simulation_Start, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_Suspend Method.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Suspend = new ExpandedNodeId(Boiler.Methods.Boilers_Boiler1_Simulation_Suspend, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_Resume Method.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Resume = new ExpandedNodeId(Boiler.Methods.Boilers_Boiler1_Simulation_Resume, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_Halt Method.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Halt = new ExpandedNodeId(Boiler.Methods.Boilers_Boiler1_Simulation_Halt, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_Reset Method.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Reset = new ExpandedNodeId(Boiler.Methods.Boilers_Boiler1_Simulation_Reset, Boiler.Namespaces.Boiler);
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
        /// The identifier for the BoilerInputPipeType_FlowTransmitter1 Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerInputPipeType_FlowTransmitter1 = new ExpandedNodeId(Boiler.Objects.BoilerInputPipeType_FlowTransmitter1, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerInputPipeType_Valve Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerInputPipeType_Valve = new ExpandedNodeId(Boiler.Objects.BoilerInputPipeType_Valve, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerDrumType_LevelIndicator Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerDrumType_LevelIndicator = new ExpandedNodeId(Boiler.Objects.BoilerDrumType_LevelIndicator, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerOutputPipeType_FlowTransmitter2 Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerOutputPipeType_FlowTransmitter2 = new ExpandedNodeId(Boiler.Objects.BoilerOutputPipeType_FlowTransmitter2, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_InputPipe Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_InputPipe = new ExpandedNodeId(Boiler.Objects.BoilerType_InputPipe, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_FlowTransmitter1 Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_InputPipe_FlowTransmitter1 = new ExpandedNodeId(Boiler.Objects.BoilerType_InputPipe_FlowTransmitter1, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_Valve Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_InputPipe_Valve = new ExpandedNodeId(Boiler.Objects.BoilerType_InputPipe_Valve, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_Drum Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum = new ExpandedNodeId(Boiler.Objects.BoilerType_Drum, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_Drum_LevelIndicator Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_LevelIndicator = new ExpandedNodeId(Boiler.Objects.BoilerType_Drum_LevelIndicator, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_OutputPipe = new ExpandedNodeId(Boiler.Objects.BoilerType_OutputPipe, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe_FlowTransmitter2 Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_OutputPipe_FlowTransmitter2 = new ExpandedNodeId(Boiler.Objects.BoilerType_OutputPipe_FlowTransmitter2, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_FlowController Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_FlowController = new ExpandedNodeId(Boiler.Objects.BoilerType_FlowController, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_LevelController Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_LevelController = new ExpandedNodeId(Boiler.Objects.BoilerType_LevelController, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController = new ExpandedNodeId(Boiler.Objects.BoilerType_CustomController, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_Simulation Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Simulation = new ExpandedNodeId(Boiler.Objects.BoilerType_Simulation, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers = new ExpandedNodeId(Boiler.Objects.Boilers, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1 Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1 = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_InputPipe, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_FlowTransmitter1 Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_FlowTransmitter1 = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_InputPipe_FlowTransmitter1, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_Valve Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_Valve = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_InputPipe_Valve, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Drum Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Drum = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_Drum, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Drum_LevelIndicator Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Drum_LevelIndicator = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_Drum_LevelIndicator, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_OutputPipe Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_OutputPipe = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_OutputPipe, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_OutputPipe_FlowTransmitter2 Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_OutputPipe_FlowTransmitter2 = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_OutputPipe_FlowTransmitter2, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_FlowController Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_FlowController = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_FlowController, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_LevelController Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_LevelController = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_LevelController, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_CustomController, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation Object.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation = new ExpandedNodeId(Boiler.Objects.Boilers_Boiler1_Simulation, Boiler.Namespaces.Boiler);
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
        /// The identifier for the GenericControllerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType = new ExpandedNodeId(Boiler.ObjectTypes.GenericControllerType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericSensorType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId GenericSensorType = new ExpandedNodeId(Boiler.ObjectTypes.GenericSensorType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericActuatorType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId GenericActuatorType = new ExpandedNodeId(Boiler.ObjectTypes.GenericActuatorType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType = new ExpandedNodeId(Boiler.ObjectTypes.CustomControllerType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the ValveType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId ValveType = new ExpandedNodeId(Boiler.ObjectTypes.ValveType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the LevelControllerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId LevelControllerType = new ExpandedNodeId(Boiler.ObjectTypes.LevelControllerType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the FlowControllerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId FlowControllerType = new ExpandedNodeId(Boiler.ObjectTypes.FlowControllerType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the LevelIndicatorType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId LevelIndicatorType = new ExpandedNodeId(Boiler.ObjectTypes.LevelIndicatorType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the FlowTransmitterType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId FlowTransmitterType = new ExpandedNodeId(Boiler.ObjectTypes.FlowTransmitterType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerStateMachineType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId BoilerStateMachineType = new ExpandedNodeId(Boiler.ObjectTypes.BoilerStateMachineType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerInputPipeType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId BoilerInputPipeType = new ExpandedNodeId(Boiler.ObjectTypes.BoilerInputPipeType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerDrumType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId BoilerDrumType = new ExpandedNodeId(Boiler.ObjectTypes.BoilerDrumType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerOutputPipeType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId BoilerOutputPipeType = new ExpandedNodeId(Boiler.ObjectTypes.BoilerOutputPipeType, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType = new ExpandedNodeId(Boiler.ObjectTypes.BoilerType, Boiler.Namespaces.Boiler);
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
        /// <summary>
        /// The identifier for the FlowTo ReferenceType.
        /// </summary>
        public static readonly ExpandedNodeId FlowTo = new ExpandedNodeId(Boiler.ReferenceTypes.FlowTo, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the HotFlowTo ReferenceType.
        /// </summary>
        public static readonly ExpandedNodeId HotFlowTo = new ExpandedNodeId(Boiler.ReferenceTypes.HotFlowTo, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the SignalTo ReferenceType.
        /// </summary>
        public static readonly ExpandedNodeId SignalTo = new ExpandedNodeId(Boiler.ReferenceTypes.SignalTo, Boiler.Namespaces.Boiler);
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
        /// The identifier for the GenericControllerType_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_Measurement = new ExpandedNodeId(Boiler.Variables.GenericControllerType_Measurement, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericControllerType_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_SetPoint = new ExpandedNodeId(Boiler.Variables.GenericControllerType_SetPoint, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericControllerType_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_ControlOut = new ExpandedNodeId(Boiler.Variables.GenericControllerType_ControlOut, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericSensorType_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericSensorType_Output = new ExpandedNodeId(Boiler.Variables.GenericSensorType_Output, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericSensorType_Output_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericSensorType_Output_EURange = new ExpandedNodeId(Boiler.Variables.GenericSensorType_Output_EURange, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericActuatorType_Input Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericActuatorType_Input = new ExpandedNodeId(Boiler.Variables.GenericActuatorType_Input, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericActuatorType_Input_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericActuatorType_Input_EURange = new ExpandedNodeId(Boiler.Variables.GenericActuatorType_Input_EURange, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType_Input1 Variable.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType_Input1 = new ExpandedNodeId(Boiler.Variables.CustomControllerType_Input1, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType_Input2 Variable.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType_Input2 = new ExpandedNodeId(Boiler.Variables.CustomControllerType_Input2, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType_Input3 Variable.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType_Input3 = new ExpandedNodeId(Boiler.Variables.CustomControllerType_Input3, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType_ControlOut = new ExpandedNodeId(Boiler.Variables.CustomControllerType_ControlOut, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType_DescriptionX Variable.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType_DescriptionX = new ExpandedNodeId(Boiler.Variables.CustomControllerType_DescriptionX, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerStateMachineType_UpdateRate Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerStateMachineType_UpdateRate = new ExpandedNodeId(Boiler.Variables.BoilerStateMachineType_UpdateRate, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_FlowTransmitter1_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_InputPipe_FlowTransmitter1_Output = new ExpandedNodeId(Boiler.Variables.BoilerType_InputPipe_FlowTransmitter1_Output, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_Valve_Input Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_InputPipe_Valve_Input = new ExpandedNodeId(Boiler.Variables.BoilerType_InputPipe_Valve_Input, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_Drum_LevelIndicator_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_LevelIndicator_Output = new ExpandedNodeId(Boiler.Variables.BoilerType_Drum_LevelIndicator_Output, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe_FlowTransmitter2_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_OutputPipe_FlowTransmitter2_Output = new ExpandedNodeId(Boiler.Variables.BoilerType_OutputPipe_FlowTransmitter2_Output, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_FlowController_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_FlowController_Measurement = new ExpandedNodeId(Boiler.Variables.BoilerType_FlowController_Measurement, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_FlowController_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_FlowController_SetPoint = new ExpandedNodeId(Boiler.Variables.BoilerType_FlowController_SetPoint, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_FlowController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_FlowController_ControlOut = new ExpandedNodeId(Boiler.Variables.BoilerType_FlowController_ControlOut, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_LevelController_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_LevelController_Measurement = new ExpandedNodeId(Boiler.Variables.BoilerType_LevelController_Measurement, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_LevelController_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_LevelController_SetPoint = new ExpandedNodeId(Boiler.Variables.BoilerType_LevelController_SetPoint, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_LevelController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_LevelController_ControlOut = new ExpandedNodeId(Boiler.Variables.BoilerType_LevelController_ControlOut, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input1 Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController_Input1 = new ExpandedNodeId(Boiler.Variables.BoilerType_CustomController_Input1, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input2 Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController_Input2 = new ExpandedNodeId(Boiler.Variables.BoilerType_CustomController_Input2, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input3 Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController_Input3 = new ExpandedNodeId(Boiler.Variables.BoilerType_CustomController_Input3, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController_ControlOut = new ExpandedNodeId(Boiler.Variables.BoilerType_CustomController_ControlOut, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController_DescriptionX Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController_DescriptionX = new ExpandedNodeId(Boiler.Variables.BoilerType_CustomController_DescriptionX, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_Simulation_UpdateRate Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Simulation_UpdateRate = new ExpandedNodeId(Boiler.Variables.BoilerType_Simulation_UpdateRate, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_FlowTransmitter1_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_FlowTransmitter1_Output = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_InputPipe_FlowTransmitter1_Output, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_FlowTransmitter1_Output_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_FlowTransmitter1_Output_EURange = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_InputPipe_FlowTransmitter1_Output_EURange, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_Valve_Input Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_Valve_Input = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_InputPipe_Valve_Input, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_InputPipe_Valve_Input_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_InputPipe_Valve_Input_EURange = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_InputPipe_Valve_Input_EURange, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Drum_LevelIndicator_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Drum_LevelIndicator_Output = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Drum_LevelIndicator_Output, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Drum_LevelIndicator_Output_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Drum_LevelIndicator_Output_EURange = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Drum_LevelIndicator_Output_EURange, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output_EURange = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_OutputPipe_FlowTransmitter2_Output_EURange, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_FlowController_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_FlowController_Measurement = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_FlowController_Measurement, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_FlowController_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_FlowController_SetPoint = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_FlowController_SetPoint, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_FlowController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_FlowController_ControlOut = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_FlowController_ControlOut, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_LevelController_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_LevelController_Measurement = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_LevelController_Measurement, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_LevelController_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_LevelController_SetPoint = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_LevelController_SetPoint, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_LevelController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_LevelController_ControlOut = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_LevelController_ControlOut, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController_Input1 Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController_Input1 = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_CustomController_Input1, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController_Input2 Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController_Input2 = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_CustomController_Input2, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController_Input3 Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController_Input3 = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_CustomController_Input3, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController_ControlOut = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_CustomController_ControlOut, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_CustomController_DescriptionX Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_CustomController_DescriptionX = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_CustomController_DescriptionX, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_CurrentState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_CurrentState = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_CurrentState, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_CurrentState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_CurrentState_Id = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_CurrentState_Id, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_CurrentState_Number Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_CurrentState_Number = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_CurrentState_Number, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_LastTransition Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_LastTransition = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_LastTransition, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_LastTransition_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_LastTransition_Id = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_LastTransition_Id, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_LastTransition_Number Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_LastTransition_Number = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_LastTransition_Number, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_LastTransition_TransitionTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_LastTransition_TransitionTime = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_LastTransition_TransitionTime, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_Deletable Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_Deletable = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_Deletable, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_RecycleCount Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_RecycleCount = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_RecycleCount, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_CreateSessionId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostics_CreateSessionId = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostics_CreateSessionId, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_CreateClientName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostics_CreateClientName = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostics_CreateClientName, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_InvocationCreationTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostics_InvocationCreationTime = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostics_InvocationCreationTime, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastTransitionTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostics_LastTransitionTime = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostics_LastTransitionTime, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodCall Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodCall = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodCall, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodSessionId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodSessionId = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodSessionId, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodInputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodInputArguments = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodInputArguments, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodOutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodOutputArguments = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodOutputArguments, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodCallTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodCallTime = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodCallTime, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodReturnStatus Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodReturnStatus = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_ProgramDiagnostics_LastMethodReturnStatus, Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boilers_Boiler1_Simulation_UpdateRate Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boilers_Boiler1_Simulation_UpdateRate = new ExpandedNodeId(Boiler.Variables.Boilers_Boiler1_Simulation_UpdateRate, Boiler.Namespaces.Boiler);
    }
    #endregion

    #region BrowseName Declarations
    /// <summary>
    /// Declares all of the BrowseNames used in the Model Design.
    /// </summary>
    public static partial class BrowseNames
    {
        /// <summary>
        /// The BrowseName for the Boiler1 component.
        /// </summary>
        public const string Boiler1 = "Boiler #1";

        /// <summary>
        /// The BrowseName for the BoilerDrumType component.
        /// </summary>
        public const string BoilerDrumType = "BoilerDrumType";

        /// <summary>
        /// The BrowseName for the BoilerInputPipeType component.
        /// </summary>
        public const string BoilerInputPipeType = "BoilerInputPipeType";

        /// <summary>
        /// The BrowseName for the BoilerOutputPipeType component.
        /// </summary>
        public const string BoilerOutputPipeType = "BoilerOutputPipeType";

        /// <summary>
        /// The BrowseName for the Boilers component.
        /// </summary>
        public const string Boilers = "Boilers";

        /// <summary>
        /// The BrowseName for the BoilerStateMachineType component.
        /// </summary>
        public const string BoilerStateMachineType = "BoilerStateMachineType";

        /// <summary>
        /// The BrowseName for the BoilerType component.
        /// </summary>
        public const string BoilerType = "BoilerType";

        /// <summary>
        /// The BrowseName for the ControlOut component.
        /// </summary>
        public const string ControlOut = "ControlOut";

        /// <summary>
        /// The BrowseName for the CustomController component.
        /// </summary>
        public const string CustomController = "CCX001";

        /// <summary>
        /// The BrowseName for the CustomControllerType component.
        /// </summary>
        public const string CustomControllerType = "CustomControllerType";

        /// <summary>
        /// The BrowseName for the DescriptionX component.
        /// </summary>
        public const string DescriptionX = "Description";

        /// <summary>
        /// The BrowseName for the Drum component.
        /// </summary>
        public const string Drum = "DrumX001";

        /// <summary>
        /// The BrowseName for the FlowController component.
        /// </summary>
        public const string FlowController = "FCX001";

        /// <summary>
        /// The BrowseName for the FlowControllerType component.
        /// </summary>
        public const string FlowControllerType = "FlowControllerType";

        /// <summary>
        /// The BrowseName for the FlowTo component.
        /// </summary>
        public const string FlowTo = "FlowTo";

        /// <summary>
        /// The BrowseName for the FlowTransmitter1 component.
        /// </summary>
        public const string FlowTransmitter1 = "FTX001";

        /// <summary>
        /// The BrowseName for the FlowTransmitter2 component.
        /// </summary>
        public const string FlowTransmitter2 = "FTX002";

        /// <summary>
        /// The BrowseName for the FlowTransmitterType component.
        /// </summary>
        public const string FlowTransmitterType = "FlowTransmitterType";

        /// <summary>
        /// The BrowseName for the GenericActuatorType component.
        /// </summary>
        public const string GenericActuatorType = "GenericActuatorType";

        /// <summary>
        /// The BrowseName for the GenericControllerType component.
        /// </summary>
        public const string GenericControllerType = "GenericControllerType";

        /// <summary>
        /// The BrowseName for the GenericSensorType component.
        /// </summary>
        public const string GenericSensorType = "GenericSensorType";

        /// <summary>
        /// The BrowseName for the HotFlowTo component.
        /// </summary>
        public const string HotFlowTo = "HotFlowTo";

        /// <summary>
        /// The BrowseName for the Input component.
        /// </summary>
        public const string Input = "Input";

        /// <summary>
        /// The BrowseName for the Input1 component.
        /// </summary>
        public const string Input1 = "Input1";

        /// <summary>
        /// The BrowseName for the Input2 component.
        /// </summary>
        public const string Input2 = "Input2";

        /// <summary>
        /// The BrowseName for the Input3 component.
        /// </summary>
        public const string Input3 = "Input3";

        /// <summary>
        /// The BrowseName for the InputPipe component.
        /// </summary>
        public const string InputPipe = "PipeX001";

        /// <summary>
        /// The BrowseName for the LevelController component.
        /// </summary>
        public const string LevelController = "LCX001";

        /// <summary>
        /// The BrowseName for the LevelControllerType component.
        /// </summary>
        public const string LevelControllerType = "LevelControllerType";

        /// <summary>
        /// The BrowseName for the LevelIndicator component.
        /// </summary>
        public const string LevelIndicator = "LIX001";

        /// <summary>
        /// The BrowseName for the LevelIndicatorType component.
        /// </summary>
        public const string LevelIndicatorType = "LevelIndicatorType";

        /// <summary>
        /// The BrowseName for the Measurement component.
        /// </summary>
        public const string Measurement = "Measurement";

        /// <summary>
        /// The BrowseName for the Output component.
        /// </summary>
        public const string Output = "Output";

        /// <summary>
        /// The BrowseName for the OutputPipe component.
        /// </summary>
        public const string OutputPipe = "PipeX002";

        /// <summary>
        /// The BrowseName for the SetPoint component.
        /// </summary>
        public const string SetPoint = "SetPoint";

        /// <summary>
        /// The BrowseName for the SignalTo component.
        /// </summary>
        public const string SignalTo = "SignalTo";

        /// <summary>
        /// The BrowseName for the Simulation component.
        /// </summary>
        public const string Simulation = "Simulation";

        /// <summary>
        /// The BrowseName for the UpdateRate component.
        /// </summary>
        public const string UpdateRate = "UpdateRate";

        /// <summary>
        /// The BrowseName for the Valve component.
        /// </summary>
        public const string Valve = "ValveX001";

        /// <summary>
        /// The BrowseName for the ValveType component.
        /// </summary>
        public const string ValveType = "ValveType";
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
        /// The URI for the Boiler namespace (.NET code namespace is 'Boiler').
        /// </summary>
        public const string Boiler = "http://opcfoundation.org/UA/Boiler/";
    }
    #endregion
}