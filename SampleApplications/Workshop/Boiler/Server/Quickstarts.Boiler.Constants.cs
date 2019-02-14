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

namespace Quickstarts.Boiler
{
    #region DataType Identifiers
    /// <summary>
    /// A class that declares constants for all DataTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypes
    {
        /// <summary>
        /// The identifier for the ControllerDataType DataType.
        /// </summary>
        public const uint ControllerDataType = 183;
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
        public const uint BoilerInputPipeType_FlowTransmitter1 = 74;

        /// <summary>
        /// The identifier for the BoilerInputPipeType_Valve Object.
        /// </summary>
        public const uint BoilerInputPipeType_Valve = 81;

        /// <summary>
        /// The identifier for the BoilerDrumType_LevelIndicator Object.
        /// </summary>
        public const uint BoilerDrumType_LevelIndicator = 89;

        /// <summary>
        /// The identifier for the BoilerOutputPipeType_FlowTransmitter2 Object.
        /// </summary>
        public const uint BoilerOutputPipeType_FlowTransmitter2 = 97;

        /// <summary>
        /// The identifier for the BoilerType_InputPipe Object.
        /// </summary>
        public const uint BoilerType_InputPipe = 56;

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_FlowTransmitter1 Object.
        /// </summary>
        public const uint BoilerType_InputPipe_FlowTransmitter1 = 104;

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_Valve Object.
        /// </summary>
        public const uint BoilerType_InputPipe_Valve = 111;

        /// <summary>
        /// The identifier for the BoilerType_Drum Object.
        /// </summary>
        public const uint BoilerType_Drum = 57;

        /// <summary>
        /// The identifier for the BoilerType_Drum_LevelIndicator Object.
        /// </summary>
        public const uint BoilerType_Drum_LevelIndicator = 58;

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe Object.
        /// </summary>
        public const uint BoilerType_OutputPipe = 65;

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe_FlowTransmitter2 Object.
        /// </summary>
        public const uint BoilerType_OutputPipe_FlowTransmitter2 = 118;

        /// <summary>
        /// The identifier for the BoilerType_FlowController Object.
        /// </summary>
        public const uint BoilerType_FlowController = 125;

        /// <summary>
        /// The identifier for the BoilerType_LevelController Object.
        /// </summary>
        public const uint BoilerType_LevelController = 129;

        /// <summary>
        /// The identifier for the BoilerType_CustomController Object.
        /// </summary>
        public const uint BoilerType_CustomController = 133;

        /// <summary>
        /// The identifier for the Boiler1 Object.
        /// </summary>
        public const uint Boiler1 = 138;

        /// <summary>
        /// The identifier for the Boiler1_InputPipe Object.
        /// </summary>
        public const uint Boiler1_InputPipe = 139;

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_FlowTransmitter1 Object.
        /// </summary>
        public const uint Boiler1_InputPipe_FlowTransmitter1 = 140;

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_Valve Object.
        /// </summary>
        public const uint Boiler1_InputPipe_Valve = 147;

        /// <summary>
        /// The identifier for the Boiler1_Drum Object.
        /// </summary>
        public const uint Boiler1_Drum = 154;

        /// <summary>
        /// The identifier for the Boiler1_Drum_LevelIndicator Object.
        /// </summary>
        public const uint Boiler1_Drum_LevelIndicator = 155;

        /// <summary>
        /// The identifier for the Boiler1_OutputPipe Object.
        /// </summary>
        public const uint Boiler1_OutputPipe = 162;

        /// <summary>
        /// The identifier for the Boiler1_OutputPipe_FlowTransmitter2 Object.
        /// </summary>
        public const uint Boiler1_OutputPipe_FlowTransmitter2 = 163;

        /// <summary>
        /// The identifier for the Boiler1_FlowController Object.
        /// </summary>
        public const uint Boiler1_FlowController = 170;

        /// <summary>
        /// The identifier for the Boiler1_LevelController Object.
        /// </summary>
        public const uint Boiler1_LevelController = 174;

        /// <summary>
        /// The identifier for the Boiler1_CustomController Object.
        /// </summary>
        public const uint Boiler1_CustomController = 178;

        /// <summary>
        /// The identifier for the ControllerDataType_Encoding_DefaultXml Object.
        /// </summary>
        public const uint ControllerDataType_Encoding_DefaultXml = 184;

        /// <summary>
        /// The identifier for the ControllerDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public const uint ControllerDataType_Encoding_DefaultBinary = 191;
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
        public const uint GenericControllerType = 3;

        /// <summary>
        /// The identifier for the GenericSensorType ObjectType.
        /// </summary>
        public const uint GenericSensorType = 7;

        /// <summary>
        /// The identifier for the GenericActuatorType ObjectType.
        /// </summary>
        public const uint GenericActuatorType = 14;

        /// <summary>
        /// The identifier for the CustomControllerType ObjectType.
        /// </summary>
        public const uint CustomControllerType = 21;

        /// <summary>
        /// The identifier for the ValveType ObjectType.
        /// </summary>
        public const uint ValveType = 26;

        /// <summary>
        /// The identifier for the LevelControllerType ObjectType.
        /// </summary>
        public const uint LevelControllerType = 33;

        /// <summary>
        /// The identifier for the FlowControllerType ObjectType.
        /// </summary>
        public const uint FlowControllerType = 37;

        /// <summary>
        /// The identifier for the LevelIndicatorType ObjectType.
        /// </summary>
        public const uint LevelIndicatorType = 41;

        /// <summary>
        /// The identifier for the FlowTransmitterType ObjectType.
        /// </summary>
        public const uint FlowTransmitterType = 48;

        /// <summary>
        /// The identifier for the BoilerInputPipeType ObjectType.
        /// </summary>
        public const uint BoilerInputPipeType = 73;

        /// <summary>
        /// The identifier for the BoilerDrumType ObjectType.
        /// </summary>
        public const uint BoilerDrumType = 88;

        /// <summary>
        /// The identifier for the BoilerOutputPipeType ObjectType.
        /// </summary>
        public const uint BoilerOutputPipeType = 96;

        /// <summary>
        /// The identifier for the BoilerType ObjectType.
        /// </summary>
        public const uint BoilerType = 55;
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
        public const uint FlowTo = 1;

        /// <summary>
        /// The identifier for the SignalTo ReferenceType.
        /// </summary>
        public const uint SignalTo = 2;
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
        public const uint GenericControllerType_Measurement = 4;

        /// <summary>
        /// The identifier for the GenericControllerType_SetPoint Variable.
        /// </summary>
        public const uint GenericControllerType_SetPoint = 5;

        /// <summary>
        /// The identifier for the GenericControllerType_ControlOut Variable.
        /// </summary>
        public const uint GenericControllerType_ControlOut = 6;

        /// <summary>
        /// The identifier for the GenericSensorType_Output Variable.
        /// </summary>
        public const uint GenericSensorType_Output = 8;

        /// <summary>
        /// The identifier for the GenericActuatorType_Input Variable.
        /// </summary>
        public const uint GenericActuatorType_Input = 15;

        /// <summary>
        /// The identifier for the CustomControllerType_Input1 Variable.
        /// </summary>
        public const uint CustomControllerType_Input1 = 22;

        /// <summary>
        /// The identifier for the CustomControllerType_Input2 Variable.
        /// </summary>
        public const uint CustomControllerType_Input2 = 23;

        /// <summary>
        /// The identifier for the CustomControllerType_Input3 Variable.
        /// </summary>
        public const uint CustomControllerType_Input3 = 24;

        /// <summary>
        /// The identifier for the CustomControllerType_ControlOut Variable.
        /// </summary>
        public const uint CustomControllerType_ControlOut = 25;

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_FlowTransmitter1_Output Variable.
        /// </summary>
        public const uint BoilerType_InputPipe_FlowTransmitter1_Output = 105;

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_Valve_Input Variable.
        /// </summary>
        public const uint BoilerType_InputPipe_Valve_Input = 112;

        /// <summary>
        /// The identifier for the BoilerType_Drum_LevelIndicator_Output Variable.
        /// </summary>
        public const uint BoilerType_Drum_LevelIndicator_Output = 59;

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe_FlowTransmitter2_Output Variable.
        /// </summary>
        public const uint BoilerType_OutputPipe_FlowTransmitter2_Output = 119;

        /// <summary>
        /// The identifier for the BoilerType_FlowController_Measurement Variable.
        /// </summary>
        public const uint BoilerType_FlowController_Measurement = 126;

        /// <summary>
        /// The identifier for the BoilerType_FlowController_SetPoint Variable.
        /// </summary>
        public const uint BoilerType_FlowController_SetPoint = 127;

        /// <summary>
        /// The identifier for the BoilerType_FlowController_ControlOut Variable.
        /// </summary>
        public const uint BoilerType_FlowController_ControlOut = 128;

        /// <summary>
        /// The identifier for the BoilerType_LevelController_Measurement Variable.
        /// </summary>
        public const uint BoilerType_LevelController_Measurement = 130;

        /// <summary>
        /// The identifier for the BoilerType_LevelController_SetPoint Variable.
        /// </summary>
        public const uint BoilerType_LevelController_SetPoint = 131;

        /// <summary>
        /// The identifier for the BoilerType_LevelController_ControlOut Variable.
        /// </summary>
        public const uint BoilerType_LevelController_ControlOut = 132;

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input1 Variable.
        /// </summary>
        public const uint BoilerType_CustomController_Input1 = 134;

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input2 Variable.
        /// </summary>
        public const uint BoilerType_CustomController_Input2 = 135;

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input3 Variable.
        /// </summary>
        public const uint BoilerType_CustomController_Input3 = 136;

        /// <summary>
        /// The identifier for the BoilerType_CustomController_ControlOut Variable.
        /// </summary>
        public const uint BoilerType_CustomController_ControlOut = 137;

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_FlowTransmitter1_Output Variable.
        /// </summary>
        public const uint Boiler1_InputPipe_FlowTransmitter1_Output = 141;

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_FlowTransmitter1_Output_EURange Variable.
        /// </summary>
        public const uint Boiler1_InputPipe_FlowTransmitter1_Output_EURange = 144;

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_Valve_Input Variable.
        /// </summary>
        public const uint Boiler1_InputPipe_Valve_Input = 148;

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_Valve_Input_EURange Variable.
        /// </summary>
        public const uint Boiler1_InputPipe_Valve_Input_EURange = 151;

        /// <summary>
        /// The identifier for the Boiler1_Drum_LevelIndicator_Output Variable.
        /// </summary>
        public const uint Boiler1_Drum_LevelIndicator_Output = 156;

        /// <summary>
        /// The identifier for the Boiler1_Drum_LevelIndicator_Output_EURange Variable.
        /// </summary>
        public const uint Boiler1_Drum_LevelIndicator_Output_EURange = 159;

        /// <summary>
        /// The identifier for the Boiler1_OutputPipe_FlowTransmitter2_Output Variable.
        /// </summary>
        public const uint Boiler1_OutputPipe_FlowTransmitter2_Output = 164;

        /// <summary>
        /// The identifier for the Boiler1_OutputPipe_FlowTransmitter2_Output_EURange Variable.
        /// </summary>
        public const uint Boiler1_OutputPipe_FlowTransmitter2_Output_EURange = 167;

        /// <summary>
        /// The identifier for the Boiler1_FlowController_Measurement Variable.
        /// </summary>
        public const uint Boiler1_FlowController_Measurement = 171;

        /// <summary>
        /// The identifier for the Boiler1_FlowController_SetPoint Variable.
        /// </summary>
        public const uint Boiler1_FlowController_SetPoint = 172;

        /// <summary>
        /// The identifier for the Boiler1_FlowController_ControlOut Variable.
        /// </summary>
        public const uint Boiler1_FlowController_ControlOut = 173;

        /// <summary>
        /// The identifier for the Boiler1_LevelController_Measurement Variable.
        /// </summary>
        public const uint Boiler1_LevelController_Measurement = 175;

        /// <summary>
        /// The identifier for the Boiler1_LevelController_SetPoint Variable.
        /// </summary>
        public const uint Boiler1_LevelController_SetPoint = 176;

        /// <summary>
        /// The identifier for the Boiler1_LevelController_ControlOut Variable.
        /// </summary>
        public const uint Boiler1_LevelController_ControlOut = 177;

        /// <summary>
        /// The identifier for the Boiler1_CustomController_Input1 Variable.
        /// </summary>
        public const uint Boiler1_CustomController_Input1 = 179;

        /// <summary>
        /// The identifier for the Boiler1_CustomController_Input2 Variable.
        /// </summary>
        public const uint Boiler1_CustomController_Input2 = 180;

        /// <summary>
        /// The identifier for the Boiler1_CustomController_Input3 Variable.
        /// </summary>
        public const uint Boiler1_CustomController_Input3 = 181;

        /// <summary>
        /// The identifier for the Boiler1_CustomController_ControlOut Variable.
        /// </summary>
        public const uint Boiler1_CustomController_ControlOut = 182;

        /// <summary>
        /// The identifier for the Boiler_XmlSchema Variable.
        /// </summary>
        public const uint Boiler_XmlSchema = 185;

        /// <summary>
        /// The identifier for the Boiler_XmlSchema_NamespaceUri Variable.
        /// </summary>
        public const uint Boiler_XmlSchema_NamespaceUri = 187;

        /// <summary>
        /// The identifier for the Boiler_XmlSchema_ControllerDataType Variable.
        /// </summary>
        public const uint Boiler_XmlSchema_ControllerDataType = 188;

        /// <summary>
        /// The identifier for the Boiler_BinarySchema Variable.
        /// </summary>
        public const uint Boiler_BinarySchema = 192;

        /// <summary>
        /// The identifier for the Boiler_BinarySchema_NamespaceUri Variable.
        /// </summary>
        public const uint Boiler_BinarySchema_NamespaceUri = 194;

        /// <summary>
        /// The identifier for the Boiler_BinarySchema_ControllerDataType Variable.
        /// </summary>
        public const uint Boiler_BinarySchema_ControllerDataType = 195;
    }
    #endregion

    #region DataType Node Identifiers
    /// <summary>
    /// A class that declares constants for all DataTypes in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypeIds
    {
        /// <summary>
        /// The identifier for the ControllerDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId ControllerDataType = new ExpandedNodeId(Quickstarts.Boiler.DataTypes.ControllerDataType, Quickstarts.Boiler.Namespaces.Boiler);
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
        public static readonly ExpandedNodeId BoilerInputPipeType_FlowTransmitter1 = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerInputPipeType_FlowTransmitter1, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerInputPipeType_Valve Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerInputPipeType_Valve = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerInputPipeType_Valve, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerDrumType_LevelIndicator Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerDrumType_LevelIndicator = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerDrumType_LevelIndicator, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerOutputPipeType_FlowTransmitter2 Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerOutputPipeType_FlowTransmitter2 = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerOutputPipeType_FlowTransmitter2, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_InputPipe Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_InputPipe = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerType_InputPipe, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_FlowTransmitter1 Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_InputPipe_FlowTransmitter1 = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerType_InputPipe_FlowTransmitter1, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_Valve Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_InputPipe_Valve = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerType_InputPipe_Valve, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_Drum Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerType_Drum, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_Drum_LevelIndicator Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_LevelIndicator = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerType_Drum_LevelIndicator, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_OutputPipe = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerType_OutputPipe, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe_FlowTransmitter2 Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_OutputPipe_FlowTransmitter2 = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerType_OutputPipe_FlowTransmitter2, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_FlowController Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_FlowController = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerType_FlowController, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_LevelController Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_LevelController = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerType_LevelController, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController Object.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController = new ExpandedNodeId(Quickstarts.Boiler.Objects.BoilerType_CustomController, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1 Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1 = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_InputPipe Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_InputPipe = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1_InputPipe, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_FlowTransmitter1 Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_InputPipe_FlowTransmitter1 = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1_InputPipe_FlowTransmitter1, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_Valve Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_InputPipe_Valve = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1_InputPipe_Valve, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_Drum Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_Drum = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1_Drum, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_Drum_LevelIndicator Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_Drum_LevelIndicator = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1_Drum_LevelIndicator, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_OutputPipe Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_OutputPipe = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1_OutputPipe, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_OutputPipe_FlowTransmitter2 Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_OutputPipe_FlowTransmitter2 = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1_OutputPipe_FlowTransmitter2, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_FlowController Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_FlowController = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1_FlowController, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_LevelController Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_LevelController = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1_LevelController, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_CustomController Object.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_CustomController = new ExpandedNodeId(Quickstarts.Boiler.Objects.Boiler1_CustomController, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the ControllerDataType_Encoding_DefaultXml Object.
        /// </summary>
        public static readonly ExpandedNodeId ControllerDataType_Encoding_DefaultXml = new ExpandedNodeId(Quickstarts.Boiler.Objects.ControllerDataType_Encoding_DefaultXml, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the ControllerDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public static readonly ExpandedNodeId ControllerDataType_Encoding_DefaultBinary = new ExpandedNodeId(Quickstarts.Boiler.Objects.ControllerDataType_Encoding_DefaultBinary, Quickstarts.Boiler.Namespaces.Boiler);
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
        public static readonly ExpandedNodeId GenericControllerType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.GenericControllerType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericSensorType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId GenericSensorType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.GenericSensorType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericActuatorType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId GenericActuatorType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.GenericActuatorType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.CustomControllerType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the ValveType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId ValveType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.ValveType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the LevelControllerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId LevelControllerType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.LevelControllerType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the FlowControllerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId FlowControllerType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.FlowControllerType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the LevelIndicatorType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId LevelIndicatorType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.LevelIndicatorType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the FlowTransmitterType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId FlowTransmitterType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.FlowTransmitterType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerInputPipeType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId BoilerInputPipeType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.BoilerInputPipeType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerDrumType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId BoilerDrumType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.BoilerDrumType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerOutputPipeType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId BoilerOutputPipeType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.BoilerOutputPipeType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType = new ExpandedNodeId(Quickstarts.Boiler.ObjectTypes.BoilerType, Quickstarts.Boiler.Namespaces.Boiler);
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
        public static readonly ExpandedNodeId FlowTo = new ExpandedNodeId(Quickstarts.Boiler.ReferenceTypes.FlowTo, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the SignalTo ReferenceType.
        /// </summary>
        public static readonly ExpandedNodeId SignalTo = new ExpandedNodeId(Quickstarts.Boiler.ReferenceTypes.SignalTo, Quickstarts.Boiler.Namespaces.Boiler);
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
        public static readonly ExpandedNodeId GenericControllerType_Measurement = new ExpandedNodeId(Quickstarts.Boiler.Variables.GenericControllerType_Measurement, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericControllerType_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_SetPoint = new ExpandedNodeId(Quickstarts.Boiler.Variables.GenericControllerType_SetPoint, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericControllerType_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericControllerType_ControlOut = new ExpandedNodeId(Quickstarts.Boiler.Variables.GenericControllerType_ControlOut, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericSensorType_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericSensorType_Output = new ExpandedNodeId(Quickstarts.Boiler.Variables.GenericSensorType_Output, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the GenericActuatorType_Input Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenericActuatorType_Input = new ExpandedNodeId(Quickstarts.Boiler.Variables.GenericActuatorType_Input, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType_Input1 Variable.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType_Input1 = new ExpandedNodeId(Quickstarts.Boiler.Variables.CustomControllerType_Input1, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType_Input2 Variable.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType_Input2 = new ExpandedNodeId(Quickstarts.Boiler.Variables.CustomControllerType_Input2, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType_Input3 Variable.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType_Input3 = new ExpandedNodeId(Quickstarts.Boiler.Variables.CustomControllerType_Input3, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the CustomControllerType_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId CustomControllerType_ControlOut = new ExpandedNodeId(Quickstarts.Boiler.Variables.CustomControllerType_ControlOut, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_FlowTransmitter1_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_InputPipe_FlowTransmitter1_Output = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_InputPipe_FlowTransmitter1_Output, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_InputPipe_Valve_Input Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_InputPipe_Valve_Input = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_InputPipe_Valve_Input, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_Drum_LevelIndicator_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_Drum_LevelIndicator_Output = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_Drum_LevelIndicator_Output, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_OutputPipe_FlowTransmitter2_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_OutputPipe_FlowTransmitter2_Output = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_OutputPipe_FlowTransmitter2_Output, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_FlowController_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_FlowController_Measurement = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_FlowController_Measurement, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_FlowController_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_FlowController_SetPoint = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_FlowController_SetPoint, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_FlowController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_FlowController_ControlOut = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_FlowController_ControlOut, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_LevelController_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_LevelController_Measurement = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_LevelController_Measurement, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_LevelController_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_LevelController_SetPoint = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_LevelController_SetPoint, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_LevelController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_LevelController_ControlOut = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_LevelController_ControlOut, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input1 Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController_Input1 = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_CustomController_Input1, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input2 Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController_Input2 = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_CustomController_Input2, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController_Input3 Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController_Input3 = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_CustomController_Input3, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the BoilerType_CustomController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId BoilerType_CustomController_ControlOut = new ExpandedNodeId(Quickstarts.Boiler.Variables.BoilerType_CustomController_ControlOut, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_FlowTransmitter1_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_InputPipe_FlowTransmitter1_Output = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_InputPipe_FlowTransmitter1_Output, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_FlowTransmitter1_Output_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_InputPipe_FlowTransmitter1_Output_EURange = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_InputPipe_FlowTransmitter1_Output_EURange, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_Valve_Input Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_InputPipe_Valve_Input = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_InputPipe_Valve_Input, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_InputPipe_Valve_Input_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_InputPipe_Valve_Input_EURange = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_InputPipe_Valve_Input_EURange, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_Drum_LevelIndicator_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_Drum_LevelIndicator_Output = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_Drum_LevelIndicator_Output, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_Drum_LevelIndicator_Output_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_Drum_LevelIndicator_Output_EURange = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_Drum_LevelIndicator_Output_EURange, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_OutputPipe_FlowTransmitter2_Output Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_OutputPipe_FlowTransmitter2_Output = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_OutputPipe_FlowTransmitter2_Output, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_OutputPipe_FlowTransmitter2_Output_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_OutputPipe_FlowTransmitter2_Output_EURange = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_OutputPipe_FlowTransmitter2_Output_EURange, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_FlowController_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_FlowController_Measurement = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_FlowController_Measurement, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_FlowController_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_FlowController_SetPoint = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_FlowController_SetPoint, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_FlowController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_FlowController_ControlOut = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_FlowController_ControlOut, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_LevelController_Measurement Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_LevelController_Measurement = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_LevelController_Measurement, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_LevelController_SetPoint Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_LevelController_SetPoint = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_LevelController_SetPoint, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_LevelController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_LevelController_ControlOut = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_LevelController_ControlOut, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_CustomController_Input1 Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_CustomController_Input1 = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_CustomController_Input1, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_CustomController_Input2 Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_CustomController_Input2 = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_CustomController_Input2, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_CustomController_Input3 Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_CustomController_Input3 = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_CustomController_Input3, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler1_CustomController_ControlOut Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler1_CustomController_ControlOut = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler1_CustomController_ControlOut, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler_XmlSchema Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler_XmlSchema = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler_XmlSchema, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler_XmlSchema_NamespaceUri Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler_XmlSchema_NamespaceUri = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler_XmlSchema_NamespaceUri, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler_XmlSchema_ControllerDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler_XmlSchema_ControllerDataType = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler_XmlSchema_ControllerDataType, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler_BinarySchema Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler_BinarySchema = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler_BinarySchema, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler_BinarySchema_NamespaceUri Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler_BinarySchema_NamespaceUri = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler_BinarySchema_NamespaceUri, Quickstarts.Boiler.Namespaces.Boiler);

        /// <summary>
        /// The identifier for the Boiler_BinarySchema_ControllerDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Boiler_BinarySchema_ControllerDataType = new ExpandedNodeId(Quickstarts.Boiler.Variables.Boiler_BinarySchema_ControllerDataType, Quickstarts.Boiler.Namespaces.Boiler);
    }
    #endregion

    #region BrowseName Declarations
    /// <summary>
    /// Declares all of the BrowseNames used in the Model Design.
    /// </summary>
    public static partial class BrowseNames
    {
        /// <summary>
        /// The BrowseName for the Boiler_BinarySchema component.
        /// </summary>
        public const string Boiler_BinarySchema = "Quickstarts.Boiler";

        /// <summary>
        /// The BrowseName for the Boiler_XmlSchema component.
        /// </summary>
        public const string Boiler_XmlSchema = "Quickstarts.Boiler";

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
        /// The BrowseName for the BoilerType component.
        /// </summary>
        public const string BoilerType = "BoilerType";

        /// <summary>
        /// The BrowseName for the ControllerDataType component.
        /// </summary>
        public const string ControllerDataType = "ControllerDataType";

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
        /// The URI for the Boiler namespace (.NET code namespace is 'Quickstarts.Boiler').
        /// </summary>
        public const string Boiler = "http://opcfoundation.org/Quickstarts/Boiler";

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
