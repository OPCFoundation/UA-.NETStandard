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

namespace TestData
{
    #region DataType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypes
    {
        /// <remarks />
        public const uint ScalarValueDataType = 9440;

        /// <remarks />
        public const uint ArrayValueDataType = 9669;

        /// <remarks />
        public const uint BooleanDataType = 9898;

        /// <remarks />
        public const uint SByteDataType = 9899;

        /// <remarks />
        public const uint ByteDataType = 9900;

        /// <remarks />
        public const uint Int16DataType = 9901;

        /// <remarks />
        public const uint UInt16DataType = 9902;

        /// <remarks />
        public const uint Int32DataType = 9903;

        /// <remarks />
        public const uint UInt32DataType = 9904;

        /// <remarks />
        public const uint Int64DataType = 9905;

        /// <remarks />
        public const uint UInt64DataType = 9906;

        /// <remarks />
        public const uint FloatDataType = 9907;

        /// <remarks />
        public const uint DoubleDataType = 9908;

        /// <remarks />
        public const uint StringDataType = 9909;

        /// <remarks />
        public const uint DateTimeDataType = 9910;

        /// <remarks />
        public const uint GuidDataType = 9911;

        /// <remarks />
        public const uint ByteStringDataType = 9912;

        /// <remarks />
        public const uint XmlElementDataType = 9913;

        /// <remarks />
        public const uint NodeIdDataType = 9914;

        /// <remarks />
        public const uint ExpandedNodeIdDataType = 9915;

        /// <remarks />
        public const uint QualifiedNameDataType = 9916;

        /// <remarks />
        public const uint LocalizedTextDataType = 9917;

        /// <remarks />
        public const uint StatusCodeDataType = 9918;

        /// <remarks />
        public const uint VariantDataType = 9919;

        /// <remarks />
        public const uint UserScalarValueDataType = 9920;

        /// <remarks />
        public const uint UserArrayValueDataType = 10006;

        /// <remarks />
        public const uint Vector = 21000;

        /// <remarks />
        public const uint WorkOrderStatusType = 21004;

        /// <remarks />
        public const uint WorkOrderType = 21005;
    }
    #endregion

    #region Method Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Methods
    {
        /// <remarks />
        public const uint TestDataObjectType_GenerateValues = 9385;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Disable = 9415;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Enable = 9414;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_AddComment = 9416;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Acknowledge = 9436;

        /// <remarks />
        public const uint TestDataVariableType_GenerateValues = 2;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Disable = 37;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Enable = 38;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_AddComment = 39;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Acknowledge = 59;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Disable = 99;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Enable = 100;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_AddComment = 101;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Acknowledge = 121;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Disable = 9482;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Enable = 9481;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AddComment = 9483;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Acknowledge = 9503;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Disable = 9566;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Enable = 9565;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AddComment = 9567;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Acknowledge = 9587;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Disable = 9711;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Enable = 9710;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AddComment = 9712;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Acknowledge = 9732;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Disable = 9795;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Enable = 9794;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AddComment = 9796;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Acknowledge = 9816;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Disable = 9953;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Enable = 9952;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AddComment = 9954;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Acknowledge = 9974;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Disable = 10039;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Enable = 10038;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AddComment = 10040;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Acknowledge = 10060;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod1 = 10093;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod2 = 10096;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod3 = 10099;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod1 = 10102;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod2 = 10105;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod3 = 10108;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod1 = 10111;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod2 = 10114;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod1 = 10117;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod2 = 10120;

        /// <remarks />
        public const uint Data_Static_Scalar_GenerateValues = 10161;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Disable = 10191;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Enable = 10190;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AddComment = 10192;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Acknowledge = 10212;

        /// <remarks />
        public const uint Data_Static_StructureScalar_GenerateValues = 189;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Disable = 224;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Enable = 225;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_AddComment = 226;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Acknowledge = 246;

        /// <remarks />
        public const uint Data_Static_Array_GenerateValues = 10245;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Disable = 10275;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Enable = 10274;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AddComment = 10276;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Acknowledge = 10296;

        /// <remarks />
        public const uint Data_Static_UserScalar_GenerateValues = 10329;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Disable = 10359;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Enable = 10358;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AddComment = 10360;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Acknowledge = 10380;

        /// <remarks />
        public const uint Data_Static_UserArray_GenerateValues = 10408;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Disable = 10438;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Enable = 10437;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AddComment = 10439;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Acknowledge = 10459;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_GenerateValues = 10487;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Disable = 10517;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Enable = 10516;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AddComment = 10518;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Acknowledge = 10538;

        /// <remarks />
        public const uint Data_Static_AnalogArray_GenerateValues = 10622;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Disable = 10652;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Enable = 10651;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AddComment = 10653;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Acknowledge = 10673;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod1 = 10756;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod2 = 10759;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod3 = 10762;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod1 = 10765;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod2 = 10768;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod3 = 10771;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod1 = 10774;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod2 = 10777;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod1 = 10780;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod2 = 10783;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_GenerateValues = 10789;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Disable = 10819;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Enable = 10818;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AddComment = 10820;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Acknowledge = 10840;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_GenerateValues = 279;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Disable = 314;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Enable = 315;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_AddComment = 316;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Acknowledge = 336;

        /// <remarks />
        public const uint Data_Dynamic_Array_GenerateValues = 10873;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Disable = 10903;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Enable = 10902;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AddComment = 10904;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Acknowledge = 10924;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_GenerateValues = 10957;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Disable = 10987;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Enable = 10986;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AddComment = 10988;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Acknowledge = 11008;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_GenerateValues = 11036;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Disable = 11066;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Enable = 11065;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AddComment = 11067;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Acknowledge = 11087;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_GenerateValues = 11115;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Disable = 11145;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Enable = 11144;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AddComment = 11146;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge = 11166;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_GenerateValues = 11250;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Disable = 11280;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Enable = 11279;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AddComment = 11281;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Acknowledge = 11301;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Disable = 11412;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Enable = 11411;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_AddComment = 11413;
    }
    #endregion

    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint TestDataObjectType_CycleComplete = 9387;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete = 4;

        /// <remarks />
        public const uint Data = 10157;

        /// <remarks />
        public const uint Data_Static = 10158;

        /// <remarks />
        public const uint Data_Static_Scalar = 10159;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete = 10163;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete = 191;

        /// <remarks />
        public const uint Data_Static_Array = 10243;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete = 10247;

        /// <remarks />
        public const uint Data_Static_UserScalar = 10327;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete = 10331;

        /// <remarks />
        public const uint Data_Static_UserArray = 10406;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete = 10410;

        /// <remarks />
        public const uint Data_Static_AnalogScalar = 10485;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete = 10489;

        /// <remarks />
        public const uint Data_Static_AnalogArray = 10620;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete = 10624;

        /// <remarks />
        public const uint Data_Static_MethodTest = 10755;

        /// <remarks />
        public const uint Data_Dynamic = 10786;

        /// <remarks />
        public const uint Data_Dynamic_Scalar = 10787;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete = 10791;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete = 281;

        /// <remarks />
        public const uint Data_Dynamic_Array = 10871;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete = 10875;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar = 10955;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete = 10959;

        /// <remarks />
        public const uint Data_Dynamic_UserArray = 11034;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete = 11038;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar = 11113;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete = 11117;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray = 11248;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete = 11252;

        /// <remarks />
        public const uint Data_Conditions = 11383;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus = 11384;

        /// <remarks />
        public const uint ScalarValueDataType_Encoding_DefaultBinary = 11437;

        /// <remarks />
        public const uint ArrayValueDataType_Encoding_DefaultBinary = 11438;

        /// <remarks />
        public const uint UserScalarValueDataType_Encoding_DefaultBinary = 11439;

        /// <remarks />
        public const uint UserArrayValueDataType_Encoding_DefaultBinary = 11440;

        /// <remarks />
        public const uint Vector_Encoding_DefaultBinary = 21008;

        /// <remarks />
        public const uint WorkOrderStatusType_Encoding_DefaultBinary = 21011;

        /// <remarks />
        public const uint WorkOrderType_Encoding_DefaultBinary = 21012;

        /// <remarks />
        public const uint ScalarValueDataType_Encoding_DefaultXml = 11418;

        /// <remarks />
        public const uint ArrayValueDataType_Encoding_DefaultXml = 11419;

        /// <remarks />
        public const uint UserScalarValueDataType_Encoding_DefaultXml = 11420;

        /// <remarks />
        public const uint UserArrayValueDataType_Encoding_DefaultXml = 11421;

        /// <remarks />
        public const uint Vector_Encoding_DefaultXml = 1036;

        /// <remarks />
        public const uint WorkOrderStatusType_Encoding_DefaultXml = 1039;

        /// <remarks />
        public const uint WorkOrderType_Encoding_DefaultXml = 1040;

        /// <remarks />
        public const uint ScalarValueDataType_Encoding_DefaultJson = 15047;

        /// <remarks />
        public const uint ArrayValueDataType_Encoding_DefaultJson = 15048;

        /// <remarks />
        public const uint UserScalarValueDataType_Encoding_DefaultJson = 15049;

        /// <remarks />
        public const uint UserArrayValueDataType_Encoding_DefaultJson = 15050;

        /// <remarks />
        public const uint Vector_Encoding_DefaultJson = 1064;

        /// <remarks />
        public const uint WorkOrderStatusType_Encoding_DefaultJson = 1067;

        /// <remarks />
        public const uint WorkOrderType_Encoding_DefaultJson = 1068;
    }
    #endregion

    #region ObjectType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <remarks />
        public const uint GenerateValuesEventType = 9371;

        /// <remarks />
        public const uint TestDataObjectType = 9383;

        /// <remarks />
        public const uint ScalarValueObjectType = 9450;

        /// <remarks />
        public const uint AnalogScalarValueObjectType = 9534;

        /// <remarks />
        public const uint ArrayValueObjectType = 9679;

        /// <remarks />
        public const uint AnalogArrayValueObjectType = 9763;

        /// <remarks />
        public const uint UserScalarValueObjectType = 9921;

        /// <remarks />
        public const uint UserArrayValueObjectType = 10007;

        /// <remarks />
        public const uint MethodTestType = 10092;

        /// <remarks />
        public const uint TestSystemConditionType = 10123;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint GenerateValuesEventType_Iterations = 9381;

        /// <remarks />
        public const uint GenerateValuesEventType_NewValueCount = 9382;

        /// <remarks />
        public const uint TestDataObjectType_SimulationActive = 9384;

        /// <remarks />
        public const uint TestDataObjectType_GenerateValues_InputArguments = 9386;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_EventId = 9388;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_EventType = 9389;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_SourceNode = 9390;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_SourceName = 9391;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Time = 9392;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ReceiveTime = 9393;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Message = 9395;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Severity = 9396;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ConditionClassId = 11578;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ConditionClassName = 11579;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ConditionName = 11557;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_BranchId = 9397;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Retain = 9398;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_EnabledState = 9399;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_EnabledState_Id = 9400;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Quality = 9405;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Quality_SourceTimestamp = 9406;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_LastSeverity = 9409;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_LastSeverity_SourceTimestamp = 9410;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Comment = 9411;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Comment_SourceTimestamp = 9412;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ClientUserId = 9413;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_AddComment_InputArguments = 9417;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_AckedState = 9420;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_AckedState_Id = 9421;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ConfirmedState_Id = 9429;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Acknowledge_InputArguments = 9437;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Confirm_InputArguments = 9439;

        /// <remarks />
        public const uint TestDataVariableType_SimulationActive = 1;

        /// <remarks />
        public const uint TestDataVariableType_GenerateValues_InputArguments = 3;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_EventId = 5;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_EventType = 6;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_SourceNode = 7;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_SourceName = 8;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Time = 9;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ReceiveTime = 10;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Message = 12;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Severity = 13;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ConditionClassId = 14;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ConditionClassName = 15;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ConditionName = 18;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_BranchId = 19;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Retain = 20;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_EnabledState = 21;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_EnabledState_Id = 22;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Quality = 30;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Quality_SourceTimestamp = 31;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_LastSeverity = 32;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_LastSeverity_SourceTimestamp = 33;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Comment = 34;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Comment_SourceTimestamp = 35;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ClientUserId = 36;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_AddComment_InputArguments = 40;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_AckedState = 41;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_AckedState_Id = 42;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ConfirmedState_Id = 51;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Acknowledge_InputArguments = 60;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Confirm_InputArguments = 62;

        /// <remarks />
        public const uint ScalarValueVariableType_GenerateValues_InputArguments = 65;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_EventId = 67;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_EventType = 68;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_SourceNode = 69;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_SourceName = 70;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Time = 71;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ReceiveTime = 72;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Message = 74;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Severity = 75;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ConditionClassId = 76;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ConditionClassName = 77;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ConditionName = 80;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_BranchId = 81;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Retain = 82;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_EnabledState = 83;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_EnabledState_Id = 84;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Quality = 92;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Quality_SourceTimestamp = 93;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_LastSeverity = 94;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_LastSeverity_SourceTimestamp = 95;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Comment = 96;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Comment_SourceTimestamp = 97;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ClientUserId = 98;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_AddComment_InputArguments = 102;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_AckedState = 103;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_AckedState_Id = 104;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ConfirmedState_Id = 113;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Acknowledge_InputArguments = 122;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Confirm_InputArguments = 124;

        /// <remarks />
        public const uint ScalarValueVariableType_BooleanValue = 1003;

        /// <remarks />
        public const uint ScalarValueVariableType_SByteValue = 1004;

        /// <remarks />
        public const uint ScalarValueVariableType_ByteValue = 1005;

        /// <remarks />
        public const uint ScalarValueVariableType_Int16Value = 1006;

        /// <remarks />
        public const uint ScalarValueVariableType_UInt16Value = 1007;

        /// <remarks />
        public const uint ScalarValueVariableType_Int32Value = 1008;

        /// <remarks />
        public const uint ScalarValueVariableType_UInt32Value = 1009;

        /// <remarks />
        public const uint ScalarValueVariableType_Int64Value = 1010;

        /// <remarks />
        public const uint ScalarValueVariableType_UInt64Value = 1011;

        /// <remarks />
        public const uint ScalarValueVariableType_FloatValue = 1012;

        /// <remarks />
        public const uint ScalarValueVariableType_DoubleValue = 1013;

        /// <remarks />
        public const uint ScalarValueVariableType_StringValue = 1014;

        /// <remarks />
        public const uint ScalarValueVariableType_DateTimeValue = 1015;

        /// <remarks />
        public const uint ScalarValueVariableType_GuidValue = 1016;

        /// <remarks />
        public const uint ScalarValueVariableType_ByteStringValue = 1017;

        /// <remarks />
        public const uint ScalarValueVariableType_XmlElementValue = 1018;

        /// <remarks />
        public const uint ScalarValueVariableType_NodeIdValue = 1019;

        /// <remarks />
        public const uint ScalarValueVariableType_ExpandedNodeIdValue = 1020;

        /// <remarks />
        public const uint ScalarValueVariableType_QualifiedNameValue = 1021;

        /// <remarks />
        public const uint ScalarValueVariableType_LocalizedTextValue = 1022;

        /// <remarks />
        public const uint ScalarValueVariableType_StatusCodeValue = 1023;

        /// <remarks />
        public const uint ScalarValueVariableType_VariantValue = 1030;

        /// <remarks />
        public const uint ScalarValueVariableType_EnumerationValue = 1031;

        /// <remarks />
        public const uint ScalarValueVariableType_StructureValue = 1032;

        /// <remarks />
        public const uint ScalarValueVariableType_NumberValue = 1033;

        /// <remarks />
        public const uint ScalarValueVariableType_IntegerValue = 1034;

        /// <remarks />
        public const uint ScalarValueVariableType_UIntegerValue = 1035;

        /// <remarks />
        public const uint ScalarValueObjectType_GenerateValues_InputArguments = 9453;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EventId = 9455;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EventType = 9456;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_SourceNode = 9457;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_SourceName = 9458;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Time = 9459;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ReceiveTime = 9460;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Message = 9462;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Severity = 9463;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConditionClassId = 11580;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConditionClassName = 11581;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConditionName = 11558;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_BranchId = 9464;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Retain = 9465;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EnabledState = 9466;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EnabledState_Id = 9467;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Quality = 9472;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = 9473;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_LastSeverity = 9476;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 9477;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Comment = 9478;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = 9479;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ClientUserId = 9480;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AddComment_InputArguments = 9484;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AckedState = 9487;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AckedState_Id = 9488;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConfirmedState_Id = 9496;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = 9504;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Confirm_InputArguments = 9506;

        /// <remarks />
        public const uint ScalarValueObjectType_BooleanValue = 9507;

        /// <remarks />
        public const uint ScalarValueObjectType_SByteValue = 9508;

        /// <remarks />
        public const uint ScalarValueObjectType_ByteValue = 9509;

        /// <remarks />
        public const uint ScalarValueObjectType_Int16Value = 9510;

        /// <remarks />
        public const uint ScalarValueObjectType_UInt16Value = 9511;

        /// <remarks />
        public const uint ScalarValueObjectType_Int32Value = 9512;

        /// <remarks />
        public const uint ScalarValueObjectType_UInt32Value = 9513;

        /// <remarks />
        public const uint ScalarValueObjectType_Int64Value = 9514;

        /// <remarks />
        public const uint ScalarValueObjectType_UInt64Value = 9515;

        /// <remarks />
        public const uint ScalarValueObjectType_FloatValue = 9516;

        /// <remarks />
        public const uint ScalarValueObjectType_DoubleValue = 9517;

        /// <remarks />
        public const uint ScalarValueObjectType_StringValue = 9518;

        /// <remarks />
        public const uint ScalarValueObjectType_DateTimeValue = 9519;

        /// <remarks />
        public const uint ScalarValueObjectType_GuidValue = 9520;

        /// <remarks />
        public const uint ScalarValueObjectType_ByteStringValue = 9521;

        /// <remarks />
        public const uint ScalarValueObjectType_XmlElementValue = 9522;

        /// <remarks />
        public const uint ScalarValueObjectType_NodeIdValue = 9523;

        /// <remarks />
        public const uint ScalarValueObjectType_ExpandedNodeIdValue = 9524;

        /// <remarks />
        public const uint ScalarValueObjectType_QualifiedNameValue = 9525;

        /// <remarks />
        public const uint ScalarValueObjectType_LocalizedTextValue = 9526;

        /// <remarks />
        public const uint ScalarValueObjectType_StatusCodeValue = 9527;

        /// <remarks />
        public const uint ScalarValueObjectType_VariantValue = 9528;

        /// <remarks />
        public const uint ScalarValueObjectType_EnumerationValue = 9529;

        /// <remarks />
        public const uint ScalarValueObjectType_StructureValue = 9530;

        /// <remarks />
        public const uint ScalarValueObjectType_NumberValue = 9531;

        /// <remarks />
        public const uint ScalarValueObjectType_IntegerValue = 9532;

        /// <remarks />
        public const uint ScalarValueObjectType_UIntegerValue = 9533;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_GenerateValues_InputArguments = 9537;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EventId = 9539;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EventType = 9540;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_SourceNode = 9541;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_SourceName = 9542;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Time = 9543;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ReceiveTime = 9544;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Message = 9546;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Severity = 9547;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConditionClassId = 11582;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConditionClassName = 11583;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConditionName = 11559;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_BranchId = 9548;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Retain = 9549;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EnabledState = 9550;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EnabledState_Id = 9551;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Quality = 9556;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = 9557;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_LastSeverity = 9560;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 9561;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Comment = 9562;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = 9563;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ClientUserId = 9564;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AddComment_InputArguments = 9568;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AckedState = 9571;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AckedState_Id = 9572;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConfirmedState_Id = 9580;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = 9588;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Confirm_InputArguments = 9590;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_SByteValue = 9591;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_SByteValue_EURange = 9594;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_ByteValue = 9597;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_ByteValue_EURange = 9600;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int16Value = 9603;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int16Value_EURange = 9606;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt16Value = 9609;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt16Value_EURange = 9612;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int32Value = 9615;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int32Value_EURange = 9618;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt32Value = 9621;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt32Value_EURange = 9624;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int64Value = 9627;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int64Value_EURange = 9630;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt64Value = 9633;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt64Value_EURange = 9636;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_FloatValue = 9639;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_FloatValue_EURange = 9642;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_DoubleValue = 9645;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_DoubleValue_EURange = 9648;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_NumberValue = 9651;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_NumberValue_EURange = 9654;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_IntegerValue = 9657;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_IntegerValue_EURange = 9660;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UIntegerValue = 9663;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UIntegerValue_EURange = 9666;

        /// <remarks />
        public const uint ArrayValueObjectType_GenerateValues_InputArguments = 9682;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EventId = 9684;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EventType = 9685;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_SourceNode = 9686;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_SourceName = 9687;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Time = 9688;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ReceiveTime = 9689;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Message = 9691;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Severity = 9692;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConditionClassId = 11584;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConditionClassName = 11585;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConditionName = 11560;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_BranchId = 9693;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Retain = 9694;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EnabledState = 9695;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EnabledState_Id = 9696;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Quality = 9701;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = 9702;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_LastSeverity = 9705;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 9706;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Comment = 9707;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = 9708;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ClientUserId = 9709;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AddComment_InputArguments = 9713;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AckedState = 9716;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AckedState_Id = 9717;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConfirmedState_Id = 9725;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = 9733;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Confirm_InputArguments = 9735;

        /// <remarks />
        public const uint ArrayValueObjectType_BooleanValue = 9736;

        /// <remarks />
        public const uint ArrayValueObjectType_SByteValue = 9737;

        /// <remarks />
        public const uint ArrayValueObjectType_ByteValue = 9738;

        /// <remarks />
        public const uint ArrayValueObjectType_Int16Value = 9739;

        /// <remarks />
        public const uint ArrayValueObjectType_UInt16Value = 9740;

        /// <remarks />
        public const uint ArrayValueObjectType_Int32Value = 9741;

        /// <remarks />
        public const uint ArrayValueObjectType_UInt32Value = 9742;

        /// <remarks />
        public const uint ArrayValueObjectType_Int64Value = 9743;

        /// <remarks />
        public const uint ArrayValueObjectType_UInt64Value = 9744;

        /// <remarks />
        public const uint ArrayValueObjectType_FloatValue = 9745;

        /// <remarks />
        public const uint ArrayValueObjectType_DoubleValue = 9746;

        /// <remarks />
        public const uint ArrayValueObjectType_StringValue = 9747;

        /// <remarks />
        public const uint ArrayValueObjectType_DateTimeValue = 9748;

        /// <remarks />
        public const uint ArrayValueObjectType_GuidValue = 9749;

        /// <remarks />
        public const uint ArrayValueObjectType_ByteStringValue = 9750;

        /// <remarks />
        public const uint ArrayValueObjectType_XmlElementValue = 9751;

        /// <remarks />
        public const uint ArrayValueObjectType_NodeIdValue = 9752;

        /// <remarks />
        public const uint ArrayValueObjectType_ExpandedNodeIdValue = 9753;

        /// <remarks />
        public const uint ArrayValueObjectType_QualifiedNameValue = 9754;

        /// <remarks />
        public const uint ArrayValueObjectType_LocalizedTextValue = 9755;

        /// <remarks />
        public const uint ArrayValueObjectType_StatusCodeValue = 9756;

        /// <remarks />
        public const uint ArrayValueObjectType_VariantValue = 9757;

        /// <remarks />
        public const uint ArrayValueObjectType_EnumerationValue = 9758;

        /// <remarks />
        public const uint ArrayValueObjectType_StructureValue = 9759;

        /// <remarks />
        public const uint ArrayValueObjectType_NumberValue = 9760;

        /// <remarks />
        public const uint ArrayValueObjectType_IntegerValue = 9761;

        /// <remarks />
        public const uint ArrayValueObjectType_UIntegerValue = 9762;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_GenerateValues_InputArguments = 9766;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EventId = 9768;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EventType = 9769;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_SourceNode = 9770;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_SourceName = 9771;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Time = 9772;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ReceiveTime = 9773;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Message = 9775;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Severity = 9776;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConditionClassId = 11586;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConditionClassName = 11587;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConditionName = 11561;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_BranchId = 9777;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Retain = 9778;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EnabledState = 9779;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EnabledState_Id = 9780;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Quality = 9785;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = 9786;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_LastSeverity = 9789;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 9790;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Comment = 9791;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = 9792;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ClientUserId = 9793;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AddComment_InputArguments = 9797;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AckedState = 9800;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AckedState_Id = 9801;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConfirmedState_Id = 9809;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = 9817;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Confirm_InputArguments = 9819;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_SByteValue = 9820;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_SByteValue_EURange = 9823;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_ByteValue = 9826;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_ByteValue_EURange = 9829;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int16Value = 9832;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int16Value_EURange = 9835;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt16Value = 9838;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt16Value_EURange = 9841;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int32Value = 9844;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int32Value_EURange = 9847;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt32Value = 9850;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt32Value_EURange = 9853;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int64Value = 9856;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int64Value_EURange = 9859;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt64Value = 9862;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt64Value_EURange = 9865;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_FloatValue = 9868;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_FloatValue_EURange = 9871;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_DoubleValue = 9874;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_DoubleValue_EURange = 9877;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_NumberValue = 9880;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_NumberValue_EURange = 9883;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_IntegerValue = 9886;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_IntegerValue_EURange = 9889;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UIntegerValue = 9892;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UIntegerValue_EURange = 9895;

        /// <remarks />
        public const uint UserScalarValueObjectType_GenerateValues_InputArguments = 9924;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EventId = 9926;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EventType = 9927;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_SourceNode = 9928;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_SourceName = 9929;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Time = 9930;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ReceiveTime = 9931;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Message = 9933;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Severity = 9934;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConditionClassId = 11588;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConditionClassName = 11589;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConditionName = 11562;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_BranchId = 9935;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Retain = 9936;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EnabledState = 9937;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EnabledState_Id = 9938;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Quality = 9943;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = 9944;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_LastSeverity = 9947;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 9948;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Comment = 9949;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = 9950;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ClientUserId = 9951;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AddComment_InputArguments = 9955;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AckedState = 9958;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AckedState_Id = 9959;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConfirmedState_Id = 9967;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = 9975;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Confirm_InputArguments = 9977;

        /// <remarks />
        public const uint UserScalarValueObjectType_BooleanValue = 9978;

        /// <remarks />
        public const uint UserScalarValueObjectType_SByteValue = 9979;

        /// <remarks />
        public const uint UserScalarValueObjectType_ByteValue = 9980;

        /// <remarks />
        public const uint UserScalarValueObjectType_Int16Value = 9981;

        /// <remarks />
        public const uint UserScalarValueObjectType_UInt16Value = 9982;

        /// <remarks />
        public const uint UserScalarValueObjectType_Int32Value = 9983;

        /// <remarks />
        public const uint UserScalarValueObjectType_UInt32Value = 9984;

        /// <remarks />
        public const uint UserScalarValueObjectType_Int64Value = 9985;

        /// <remarks />
        public const uint UserScalarValueObjectType_UInt64Value = 9986;

        /// <remarks />
        public const uint UserScalarValueObjectType_FloatValue = 9987;

        /// <remarks />
        public const uint UserScalarValueObjectType_DoubleValue = 9988;

        /// <remarks />
        public const uint UserScalarValueObjectType_StringValue = 9989;

        /// <remarks />
        public const uint UserScalarValueObjectType_DateTimeValue = 9990;

        /// <remarks />
        public const uint UserScalarValueObjectType_GuidValue = 9991;

        /// <remarks />
        public const uint UserScalarValueObjectType_ByteStringValue = 9992;

        /// <remarks />
        public const uint UserScalarValueObjectType_XmlElementValue = 9993;

        /// <remarks />
        public const uint UserScalarValueObjectType_NodeIdValue = 9994;

        /// <remarks />
        public const uint UserScalarValueObjectType_ExpandedNodeIdValue = 9995;

        /// <remarks />
        public const uint UserScalarValueObjectType_QualifiedNameValue = 9996;

        /// <remarks />
        public const uint UserScalarValueObjectType_LocalizedTextValue = 9997;

        /// <remarks />
        public const uint UserScalarValueObjectType_StatusCodeValue = 9998;

        /// <remarks />
        public const uint UserScalarValueObjectType_VariantValue = 9999;

        /// <remarks />
        public const uint UserArrayValueObjectType_GenerateValues_InputArguments = 10010;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EventId = 10012;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EventType = 10013;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_SourceNode = 10014;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_SourceName = 10015;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Time = 10016;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ReceiveTime = 10017;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Message = 10019;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Severity = 10020;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConditionClassId = 11590;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConditionClassName = 11591;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConditionName = 11563;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_BranchId = 10021;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Retain = 10022;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EnabledState = 10023;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EnabledState_Id = 10024;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Quality = 10029;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = 10030;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_LastSeverity = 10033;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 10034;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Comment = 10035;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = 10036;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ClientUserId = 10037;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AddComment_InputArguments = 10041;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AckedState = 10044;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AckedState_Id = 10045;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConfirmedState_Id = 10053;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = 10061;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Confirm_InputArguments = 10063;

        /// <remarks />
        public const uint UserArrayValueObjectType_BooleanValue = 10064;

        /// <remarks />
        public const uint UserArrayValueObjectType_SByteValue = 10065;

        /// <remarks />
        public const uint UserArrayValueObjectType_ByteValue = 10066;

        /// <remarks />
        public const uint UserArrayValueObjectType_Int16Value = 10067;

        /// <remarks />
        public const uint UserArrayValueObjectType_UInt16Value = 10068;

        /// <remarks />
        public const uint UserArrayValueObjectType_Int32Value = 10069;

        /// <remarks />
        public const uint UserArrayValueObjectType_UInt32Value = 10070;

        /// <remarks />
        public const uint UserArrayValueObjectType_Int64Value = 10071;

        /// <remarks />
        public const uint UserArrayValueObjectType_UInt64Value = 10072;

        /// <remarks />
        public const uint UserArrayValueObjectType_FloatValue = 10073;

        /// <remarks />
        public const uint UserArrayValueObjectType_DoubleValue = 10074;

        /// <remarks />
        public const uint UserArrayValueObjectType_StringValue = 10075;

        /// <remarks />
        public const uint UserArrayValueObjectType_DateTimeValue = 10076;

        /// <remarks />
        public const uint UserArrayValueObjectType_GuidValue = 10077;

        /// <remarks />
        public const uint UserArrayValueObjectType_ByteStringValue = 10078;

        /// <remarks />
        public const uint UserArrayValueObjectType_XmlElementValue = 10079;

        /// <remarks />
        public const uint UserArrayValueObjectType_NodeIdValue = 10080;

        /// <remarks />
        public const uint UserArrayValueObjectType_ExpandedNodeIdValue = 10081;

        /// <remarks />
        public const uint UserArrayValueObjectType_QualifiedNameValue = 10082;

        /// <remarks />
        public const uint UserArrayValueObjectType_LocalizedTextValue = 10083;

        /// <remarks />
        public const uint UserArrayValueObjectType_StatusCodeValue = 10084;

        /// <remarks />
        public const uint UserArrayValueObjectType_VariantValue = 10085;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod1_InputArguments = 10094;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod1_OutputArguments = 10095;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod2_InputArguments = 10097;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod2_OutputArguments = 10098;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod3_InputArguments = 10100;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod3_OutputArguments = 10101;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod1_InputArguments = 10103;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod1_OutputArguments = 10104;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod2_InputArguments = 10106;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod2_OutputArguments = 10107;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod3_InputArguments = 10109;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod3_OutputArguments = 10110;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod1_InputArguments = 10112;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod1_OutputArguments = 10113;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod2_InputArguments = 10115;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod2_OutputArguments = 10116;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod1_InputArguments = 10118;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod1_OutputArguments = 10119;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod2_InputArguments = 10121;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod2_OutputArguments = 10122;

        /// <remarks />
        public const uint TestSystemConditionType_EnabledState_Id = 10136;

        /// <remarks />
        public const uint TestSystemConditionType_Quality_SourceTimestamp = 10142;

        /// <remarks />
        public const uint TestSystemConditionType_LastSeverity_SourceTimestamp = 10146;

        /// <remarks />
        public const uint TestSystemConditionType_Comment_SourceTimestamp = 10148;

        /// <remarks />
        public const uint TestSystemConditionType_AddComment_InputArguments = 10153;

        /// <remarks />
        public const uint TestSystemConditionType_ConditionRefresh_InputArguments = 10155;

        /// <remarks />
        public const uint TestSystemConditionType_ConditionRefresh2_InputArguments = 15018;

        /// <remarks />
        public const uint TestSystemConditionType_MonitoredNodeCount = 10156;

        /// <remarks />
        public const uint Data_Static_Scalar_SimulationActive = 10160;

        /// <remarks />
        public const uint Data_Static_Scalar_GenerateValues_InputArguments = 10162;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EventId = 10164;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EventType = 10165;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_SourceNode = 10166;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_SourceName = 10167;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Time = 10168;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ReceiveTime = 10169;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Message = 10171;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Severity = 10172;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConditionClassId = 11594;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConditionClassName = 11595;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConditionName = 11565;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_BranchId = 10173;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Retain = 10174;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EnabledState = 10175;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EnabledState_Id = 10176;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Quality = 10181;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Quality_SourceTimestamp = 10182;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_LastSeverity = 10185;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_LastSeverity_SourceTimestamp = 10186;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Comment = 10187;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Comment_SourceTimestamp = 10188;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ClientUserId = 10189;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AddComment_InputArguments = 10193;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AckedState = 10196;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AckedState_Id = 10197;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConfirmedState_Id = 10205;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Acknowledge_InputArguments = 10213;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Confirm_InputArguments = 10215;

        /// <remarks />
        public const uint Data_Static_Scalar_BooleanValue = 10216;

        /// <remarks />
        public const uint Data_Static_Scalar_SByteValue = 10217;

        /// <remarks />
        public const uint Data_Static_Scalar_ByteValue = 10218;

        /// <remarks />
        public const uint Data_Static_Scalar_Int16Value = 10219;

        /// <remarks />
        public const uint Data_Static_Scalar_UInt16Value = 10220;

        /// <remarks />
        public const uint Data_Static_Scalar_Int32Value = 10221;

        /// <remarks />
        public const uint Data_Static_Scalar_UInt32Value = 10222;

        /// <remarks />
        public const uint Data_Static_Scalar_Int64Value = 10223;

        /// <remarks />
        public const uint Data_Static_Scalar_UInt64Value = 10224;

        /// <remarks />
        public const uint Data_Static_Scalar_FloatValue = 10225;

        /// <remarks />
        public const uint Data_Static_Scalar_DoubleValue = 10226;

        /// <remarks />
        public const uint Data_Static_Scalar_StringValue = 10227;

        /// <remarks />
        public const uint Data_Static_Scalar_DateTimeValue = 10228;

        /// <remarks />
        public const uint Data_Static_Scalar_GuidValue = 10229;

        /// <remarks />
        public const uint Data_Static_Scalar_ByteStringValue = 10230;

        /// <remarks />
        public const uint Data_Static_Scalar_XmlElementValue = 10231;

        /// <remarks />
        public const uint Data_Static_Scalar_NodeIdValue = 10232;

        /// <remarks />
        public const uint Data_Static_Scalar_ExpandedNodeIdValue = 10233;

        /// <remarks />
        public const uint Data_Static_Scalar_QualifiedNameValue = 10234;

        /// <remarks />
        public const uint Data_Static_Scalar_LocalizedTextValue = 10235;

        /// <remarks />
        public const uint Data_Static_Scalar_StatusCodeValue = 10236;

        /// <remarks />
        public const uint Data_Static_Scalar_VariantValue = 10237;

        /// <remarks />
        public const uint Data_Static_Scalar_EnumerationValue = 10238;

        /// <remarks />
        public const uint Data_Static_Scalar_StructureValue = 10239;

        /// <remarks />
        public const uint Data_Static_Scalar_NumberValue = 10240;

        /// <remarks />
        public const uint Data_Static_Scalar_IntegerValue = 10241;

        /// <remarks />
        public const uint Data_Static_Scalar_UIntegerValue = 10242;

        /// <remarks />
        public const uint Data_Static_StructureScalar = 187;

        /// <remarks />
        public const uint Data_Static_StructureScalar_SimulationActive = 188;

        /// <remarks />
        public const uint Data_Static_StructureScalar_GenerateValues_InputArguments = 190;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_EventId = 192;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_EventType = 193;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_SourceNode = 194;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_SourceName = 195;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Time = 196;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ReceiveTime = 197;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Message = 199;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Severity = 200;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ConditionClassId = 201;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ConditionClassName = 202;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ConditionName = 205;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_BranchId = 206;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Retain = 207;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_EnabledState = 208;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_EnabledState_Id = 209;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Quality = 217;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Quality_SourceTimestamp = 218;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_LastSeverity = 219;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_LastSeverity_SourceTimestamp = 220;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Comment = 221;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Comment_SourceTimestamp = 222;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ClientUserId = 223;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_AddComment_InputArguments = 227;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_AckedState = 228;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_AckedState_Id = 229;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ConfirmedState_Id = 238;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Acknowledge_InputArguments = 247;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Confirm_InputArguments = 249;

        /// <remarks />
        public const uint Data_Static_StructureScalar_BooleanValue = 250;

        /// <remarks />
        public const uint Data_Static_StructureScalar_SByteValue = 251;

        /// <remarks />
        public const uint Data_Static_StructureScalar_ByteValue = 252;

        /// <remarks />
        public const uint Data_Static_StructureScalar_Int16Value = 253;

        /// <remarks />
        public const uint Data_Static_StructureScalar_UInt16Value = 254;

        /// <remarks />
        public const uint Data_Static_StructureScalar_Int32Value = 255;

        /// <remarks />
        public const uint Data_Static_StructureScalar_UInt32Value = 256;

        /// <remarks />
        public const uint Data_Static_StructureScalar_Int64Value = 257;

        /// <remarks />
        public const uint Data_Static_StructureScalar_UInt64Value = 258;

        /// <remarks />
        public const uint Data_Static_StructureScalar_FloatValue = 259;

        /// <remarks />
        public const uint Data_Static_StructureScalar_DoubleValue = 260;

        /// <remarks />
        public const uint Data_Static_StructureScalar_StringValue = 261;

        /// <remarks />
        public const uint Data_Static_StructureScalar_DateTimeValue = 262;

        /// <remarks />
        public const uint Data_Static_StructureScalar_GuidValue = 263;

        /// <remarks />
        public const uint Data_Static_StructureScalar_ByteStringValue = 264;

        /// <remarks />
        public const uint Data_Static_StructureScalar_XmlElementValue = 265;

        /// <remarks />
        public const uint Data_Static_StructureScalar_NodeIdValue = 266;

        /// <remarks />
        public const uint Data_Static_StructureScalar_ExpandedNodeIdValue = 267;

        /// <remarks />
        public const uint Data_Static_StructureScalar_QualifiedNameValue = 268;

        /// <remarks />
        public const uint Data_Static_StructureScalar_LocalizedTextValue = 269;

        /// <remarks />
        public const uint Data_Static_StructureScalar_StatusCodeValue = 270;

        /// <remarks />
        public const uint Data_Static_StructureScalar_VariantValue = 271;

        /// <remarks />
        public const uint Data_Static_StructureScalar_EnumerationValue = 272;

        /// <remarks />
        public const uint Data_Static_StructureScalar_StructureValue = 273;

        /// <remarks />
        public const uint Data_Static_StructureScalar_NumberValue = 274;

        /// <remarks />
        public const uint Data_Static_StructureScalar_IntegerValue = 275;

        /// <remarks />
        public const uint Data_Static_StructureScalar_UIntegerValue = 276;

        /// <remarks />
        public const uint Data_Static_Array_SimulationActive = 10244;

        /// <remarks />
        public const uint Data_Static_Array_GenerateValues_InputArguments = 10246;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EventId = 10248;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EventType = 10249;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_SourceNode = 10250;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_SourceName = 10251;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Time = 10252;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ReceiveTime = 10253;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Message = 10255;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Severity = 10256;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConditionClassId = 11596;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConditionClassName = 11597;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConditionName = 11566;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_BranchId = 10257;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Retain = 10258;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EnabledState = 10259;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EnabledState_Id = 10260;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Quality = 10265;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Quality_SourceTimestamp = 10266;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_LastSeverity = 10269;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_LastSeverity_SourceTimestamp = 10270;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Comment = 10271;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Comment_SourceTimestamp = 10272;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ClientUserId = 10273;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AddComment_InputArguments = 10277;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AckedState = 10280;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AckedState_Id = 10281;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConfirmedState_Id = 10289;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Acknowledge_InputArguments = 10297;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Confirm_InputArguments = 10299;

        /// <remarks />
        public const uint Data_Static_Array_BooleanValue = 10300;

        /// <remarks />
        public const uint Data_Static_Array_SByteValue = 10301;

        /// <remarks />
        public const uint Data_Static_Array_ByteValue = 10302;

        /// <remarks />
        public const uint Data_Static_Array_Int16Value = 10303;

        /// <remarks />
        public const uint Data_Static_Array_UInt16Value = 10304;

        /// <remarks />
        public const uint Data_Static_Array_Int32Value = 10305;

        /// <remarks />
        public const uint Data_Static_Array_UInt32Value = 10306;

        /// <remarks />
        public const uint Data_Static_Array_Int64Value = 10307;

        /// <remarks />
        public const uint Data_Static_Array_UInt64Value = 10308;

        /// <remarks />
        public const uint Data_Static_Array_FloatValue = 10309;

        /// <remarks />
        public const uint Data_Static_Array_DoubleValue = 10310;

        /// <remarks />
        public const uint Data_Static_Array_StringValue = 10311;

        /// <remarks />
        public const uint Data_Static_Array_DateTimeValue = 10312;

        /// <remarks />
        public const uint Data_Static_Array_GuidValue = 10313;

        /// <remarks />
        public const uint Data_Static_Array_ByteStringValue = 10314;

        /// <remarks />
        public const uint Data_Static_Array_XmlElementValue = 10315;

        /// <remarks />
        public const uint Data_Static_Array_NodeIdValue = 10316;

        /// <remarks />
        public const uint Data_Static_Array_ExpandedNodeIdValue = 10317;

        /// <remarks />
        public const uint Data_Static_Array_QualifiedNameValue = 10318;

        /// <remarks />
        public const uint Data_Static_Array_LocalizedTextValue = 10319;

        /// <remarks />
        public const uint Data_Static_Array_StatusCodeValue = 10320;

        /// <remarks />
        public const uint Data_Static_Array_VariantValue = 10321;

        /// <remarks />
        public const uint Data_Static_Array_EnumerationValue = 10322;

        /// <remarks />
        public const uint Data_Static_Array_StructureValue = 10323;

        /// <remarks />
        public const uint Data_Static_Array_NumberValue = 10324;

        /// <remarks />
        public const uint Data_Static_Array_IntegerValue = 10325;

        /// <remarks />
        public const uint Data_Static_Array_UIntegerValue = 10326;

        /// <remarks />
        public const uint Data_Static_UserScalar_SimulationActive = 10328;

        /// <remarks />
        public const uint Data_Static_UserScalar_GenerateValues_InputArguments = 10330;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EventId = 10332;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EventType = 10333;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_SourceNode = 10334;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_SourceName = 10335;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Time = 10336;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ReceiveTime = 10337;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Message = 10339;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Severity = 10340;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConditionClassId = 11598;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConditionClassName = 11599;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConditionName = 11567;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_BranchId = 10341;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Retain = 10342;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EnabledState = 10343;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EnabledState_Id = 10344;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Quality = 10349;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Quality_SourceTimestamp = 10350;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_LastSeverity = 10353;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = 10354;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Comment = 10355;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Comment_SourceTimestamp = 10356;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ClientUserId = 10357;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AddComment_InputArguments = 10361;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AckedState = 10364;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AckedState_Id = 10365;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConfirmedState_Id = 10373;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Acknowledge_InputArguments = 10381;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Confirm_InputArguments = 10383;

        /// <remarks />
        public const uint Data_Static_UserScalar_BooleanValue = 10384;

        /// <remarks />
        public const uint Data_Static_UserScalar_SByteValue = 10385;

        /// <remarks />
        public const uint Data_Static_UserScalar_ByteValue = 10386;

        /// <remarks />
        public const uint Data_Static_UserScalar_Int16Value = 10387;

        /// <remarks />
        public const uint Data_Static_UserScalar_UInt16Value = 10388;

        /// <remarks />
        public const uint Data_Static_UserScalar_Int32Value = 10389;

        /// <remarks />
        public const uint Data_Static_UserScalar_UInt32Value = 10390;

        /// <remarks />
        public const uint Data_Static_UserScalar_Int64Value = 10391;

        /// <remarks />
        public const uint Data_Static_UserScalar_UInt64Value = 10392;

        /// <remarks />
        public const uint Data_Static_UserScalar_FloatValue = 10393;

        /// <remarks />
        public const uint Data_Static_UserScalar_DoubleValue = 10394;

        /// <remarks />
        public const uint Data_Static_UserScalar_StringValue = 10395;

        /// <remarks />
        public const uint Data_Static_UserScalar_DateTimeValue = 10396;

        /// <remarks />
        public const uint Data_Static_UserScalar_GuidValue = 10397;

        /// <remarks />
        public const uint Data_Static_UserScalar_ByteStringValue = 10398;

        /// <remarks />
        public const uint Data_Static_UserScalar_XmlElementValue = 10399;

        /// <remarks />
        public const uint Data_Static_UserScalar_NodeIdValue = 10400;

        /// <remarks />
        public const uint Data_Static_UserScalar_ExpandedNodeIdValue = 10401;

        /// <remarks />
        public const uint Data_Static_UserScalar_QualifiedNameValue = 10402;

        /// <remarks />
        public const uint Data_Static_UserScalar_LocalizedTextValue = 10403;

        /// <remarks />
        public const uint Data_Static_UserScalar_StatusCodeValue = 10404;

        /// <remarks />
        public const uint Data_Static_UserScalar_VariantValue = 10405;

        /// <remarks />
        public const uint Data_Static_UserArray_SimulationActive = 10407;

        /// <remarks />
        public const uint Data_Static_UserArray_GenerateValues_InputArguments = 10409;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EventId = 10411;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EventType = 10412;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_SourceNode = 10413;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_SourceName = 10414;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Time = 10415;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ReceiveTime = 10416;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Message = 10418;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Severity = 10419;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConditionClassId = 11600;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConditionClassName = 11601;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConditionName = 11568;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_BranchId = 10420;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Retain = 10421;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EnabledState = 10422;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EnabledState_Id = 10423;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Quality = 10428;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Quality_SourceTimestamp = 10429;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_LastSeverity = 10432;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_LastSeverity_SourceTimestamp = 10433;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Comment = 10434;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Comment_SourceTimestamp = 10435;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ClientUserId = 10436;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AddComment_InputArguments = 10440;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AckedState = 10443;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AckedState_Id = 10444;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConfirmedState_Id = 10452;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Acknowledge_InputArguments = 10460;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Confirm_InputArguments = 10462;

        /// <remarks />
        public const uint Data_Static_UserArray_BooleanValue = 10463;

        /// <remarks />
        public const uint Data_Static_UserArray_SByteValue = 10464;

        /// <remarks />
        public const uint Data_Static_UserArray_ByteValue = 10465;

        /// <remarks />
        public const uint Data_Static_UserArray_Int16Value = 10466;

        /// <remarks />
        public const uint Data_Static_UserArray_UInt16Value = 10467;

        /// <remarks />
        public const uint Data_Static_UserArray_Int32Value = 10468;

        /// <remarks />
        public const uint Data_Static_UserArray_UInt32Value = 10469;

        /// <remarks />
        public const uint Data_Static_UserArray_Int64Value = 10470;

        /// <remarks />
        public const uint Data_Static_UserArray_UInt64Value = 10471;

        /// <remarks />
        public const uint Data_Static_UserArray_FloatValue = 10472;

        /// <remarks />
        public const uint Data_Static_UserArray_DoubleValue = 10473;

        /// <remarks />
        public const uint Data_Static_UserArray_StringValue = 10474;

        /// <remarks />
        public const uint Data_Static_UserArray_DateTimeValue = 10475;

        /// <remarks />
        public const uint Data_Static_UserArray_GuidValue = 10476;

        /// <remarks />
        public const uint Data_Static_UserArray_ByteStringValue = 10477;

        /// <remarks />
        public const uint Data_Static_UserArray_XmlElementValue = 10478;

        /// <remarks />
        public const uint Data_Static_UserArray_NodeIdValue = 10479;

        /// <remarks />
        public const uint Data_Static_UserArray_ExpandedNodeIdValue = 10480;

        /// <remarks />
        public const uint Data_Static_UserArray_QualifiedNameValue = 10481;

        /// <remarks />
        public const uint Data_Static_UserArray_LocalizedTextValue = 10482;

        /// <remarks />
        public const uint Data_Static_UserArray_StatusCodeValue = 10483;

        /// <remarks />
        public const uint Data_Static_UserArray_VariantValue = 10484;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_SimulationActive = 10486;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_GenerateValues_InputArguments = 10488;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EventId = 10490;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EventType = 10491;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_SourceNode = 10492;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_SourceName = 10493;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Time = 10494;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ReceiveTime = 10495;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Message = 10497;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Severity = 10498;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionClassId = 11602;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionClassName = 11603;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionName = 11569;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_BranchId = 10499;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Retain = 10500;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EnabledState = 10501;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EnabledState_Id = 10502;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Quality = 10507;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Quality_SourceTimestamp = 10508;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_LastSeverity = 10511;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = 10512;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Comment = 10513;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Comment_SourceTimestamp = 10514;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ClientUserId = 10515;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AddComment_InputArguments = 10519;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AckedState = 10522;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AckedState_Id = 10523;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConfirmedState_Id = 10531;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Acknowledge_InputArguments = 10539;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Confirm_InputArguments = 10541;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_SByteValue = 10542;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_SByteValue_EURange = 10545;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_ByteValue = 10548;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_ByteValue_EURange = 10551;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int16Value = 10554;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int16Value_EURange = 10557;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt16Value = 10560;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt16Value_EURange = 10563;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int32Value = 10566;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int32Value_EURange = 10569;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt32Value = 10572;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt32Value_EURange = 10575;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int64Value = 10578;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int64Value_EURange = 10581;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt64Value = 10584;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt64Value_EURange = 10587;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_FloatValue = 10590;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_FloatValue_EURange = 10593;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_DoubleValue = 10596;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_DoubleValue_EURange = 10599;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_NumberValue = 10602;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_NumberValue_EURange = 10605;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_IntegerValue = 10608;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_IntegerValue_EURange = 10611;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UIntegerValue = 10614;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UIntegerValue_EURange = 10617;

        /// <remarks />
        public const uint Data_Static_AnalogArray_SimulationActive = 10621;

        /// <remarks />
        public const uint Data_Static_AnalogArray_GenerateValues_InputArguments = 10623;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EventId = 10625;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EventType = 10626;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_SourceNode = 10627;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_SourceName = 10628;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Time = 10629;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ReceiveTime = 10630;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Message = 10632;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Severity = 10633;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionClassId = 11604;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionClassName = 11605;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionName = 11570;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_BranchId = 10634;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Retain = 10635;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EnabledState = 10636;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EnabledState_Id = 10637;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Quality = 10642;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Quality_SourceTimestamp = 10643;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_LastSeverity = 10646;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = 10647;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Comment = 10648;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Comment_SourceTimestamp = 10649;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ClientUserId = 10650;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AddComment_InputArguments = 10654;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AckedState = 10657;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AckedState_Id = 10658;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConfirmedState_Id = 10666;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Acknowledge_InputArguments = 10674;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Confirm_InputArguments = 10676;

        /// <remarks />
        public const uint Data_Static_AnalogArray_SByteValue = 10677;

        /// <remarks />
        public const uint Data_Static_AnalogArray_SByteValue_EURange = 10680;

        /// <remarks />
        public const uint Data_Static_AnalogArray_ByteValue = 10683;

        /// <remarks />
        public const uint Data_Static_AnalogArray_ByteValue_EURange = 10686;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int16Value = 10689;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int16Value_EURange = 10692;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt16Value = 10695;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt16Value_EURange = 10698;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int32Value = 10701;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int32Value_EURange = 10704;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt32Value = 10707;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt32Value_EURange = 10710;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int64Value = 10713;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int64Value_EURange = 10716;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt64Value = 10719;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt64Value_EURange = 10722;

        /// <remarks />
        public const uint Data_Static_AnalogArray_FloatValue = 10725;

        /// <remarks />
        public const uint Data_Static_AnalogArray_FloatValue_EURange = 10728;

        /// <remarks />
        public const uint Data_Static_AnalogArray_DoubleValue = 10731;

        /// <remarks />
        public const uint Data_Static_AnalogArray_DoubleValue_EURange = 10734;

        /// <remarks />
        public const uint Data_Static_AnalogArray_NumberValue = 10737;

        /// <remarks />
        public const uint Data_Static_AnalogArray_NumberValue_EURange = 10740;

        /// <remarks />
        public const uint Data_Static_AnalogArray_IntegerValue = 10743;

        /// <remarks />
        public const uint Data_Static_AnalogArray_IntegerValue_EURange = 10746;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UIntegerValue = 10749;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UIntegerValue_EURange = 10752;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod1_InputArguments = 10757;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod1_OutputArguments = 10758;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod2_InputArguments = 10760;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod2_OutputArguments = 10761;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod3_InputArguments = 10763;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod3_OutputArguments = 10764;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod1_InputArguments = 10766;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod1_OutputArguments = 10767;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod2_InputArguments = 10769;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod2_OutputArguments = 10770;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod3_InputArguments = 10772;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod3_OutputArguments = 10773;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod1_InputArguments = 10775;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod1_OutputArguments = 10776;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod2_InputArguments = 10778;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod2_OutputArguments = 10779;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod1_InputArguments = 10781;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod1_OutputArguments = 10782;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod2_InputArguments = 10784;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod2_OutputArguments = 10785;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_SimulationActive = 10788;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_GenerateValues_InputArguments = 10790;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EventId = 10792;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EventType = 10793;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_SourceNode = 10794;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_SourceName = 10795;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Time = 10796;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ReceiveTime = 10797;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Message = 10799;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Severity = 10800;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionClassId = 11606;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionClassName = 11607;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionName = 11571;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_BranchId = 10801;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Retain = 10802;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EnabledState = 10803;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EnabledState_Id = 10804;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Quality = 10809;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Quality_SourceTimestamp = 10810;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_LastSeverity = 10813;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_LastSeverity_SourceTimestamp = 10814;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Comment = 10815;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Comment_SourceTimestamp = 10816;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ClientUserId = 10817;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AddComment_InputArguments = 10821;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AckedState = 10824;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AckedState_Id = 10825;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConfirmedState_Id = 10833;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Acknowledge_InputArguments = 10841;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Confirm_InputArguments = 10843;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_BooleanValue = 10844;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_SByteValue = 10845;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_ByteValue = 10846;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_Int16Value = 10847;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UInt16Value = 10848;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_Int32Value = 10849;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UInt32Value = 10850;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_Int64Value = 10851;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UInt64Value = 10852;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_FloatValue = 10853;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_DoubleValue = 10854;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_StringValue = 10855;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_DateTimeValue = 10856;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_GuidValue = 10857;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_ByteStringValue = 10858;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_XmlElementValue = 10859;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_NodeIdValue = 10860;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_ExpandedNodeIdValue = 10861;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_QualifiedNameValue = 10862;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_LocalizedTextValue = 10863;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_StatusCodeValue = 10864;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VariantValue = 10865;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_EnumerationValue = 10866;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_StructureValue = 10867;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_NumberValue = 10868;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_IntegerValue = 10869;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UIntegerValue = 10870;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar = 277;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_SimulationActive = 278;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_GenerateValues_InputArguments = 280;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_EventId = 282;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_EventType = 283;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_SourceNode = 284;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_SourceName = 285;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Time = 286;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ReceiveTime = 287;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Message = 289;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Severity = 290;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ConditionClassId = 291;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ConditionClassName = 292;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ConditionName = 295;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_BranchId = 296;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Retain = 297;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_EnabledState = 298;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_EnabledState_Id = 299;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Quality = 307;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Quality_SourceTimestamp = 308;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_LastSeverity = 309;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_LastSeverity_SourceTimestamp = 310;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Comment = 311;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Comment_SourceTimestamp = 312;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ClientUserId = 313;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_AddComment_InputArguments = 317;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_AckedState = 318;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_AckedState_Id = 319;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ConfirmedState_Id = 328;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Acknowledge_InputArguments = 337;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Confirm_InputArguments = 339;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_BooleanValue = 340;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_SByteValue = 341;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_ByteValue = 342;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_Int16Value = 343;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_UInt16Value = 344;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_Int32Value = 345;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_UInt32Value = 346;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_Int64Value = 347;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_UInt64Value = 348;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_FloatValue = 349;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_DoubleValue = 350;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_StringValue = 351;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_DateTimeValue = 352;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_GuidValue = 353;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_ByteStringValue = 354;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_XmlElementValue = 355;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_NodeIdValue = 356;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_ExpandedNodeIdValue = 357;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_QualifiedNameValue = 358;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_LocalizedTextValue = 359;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_StatusCodeValue = 360;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_VariantValue = 361;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_EnumerationValue = 362;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_StructureValue = 363;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_NumberValue = 364;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_IntegerValue = 365;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_UIntegerValue = 366;

        /// <remarks />
        public const uint Data_Dynamic_Array_SimulationActive = 10872;

        /// <remarks />
        public const uint Data_Dynamic_Array_GenerateValues_InputArguments = 10874;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EventId = 10876;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EventType = 10877;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_SourceNode = 10878;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_SourceName = 10879;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Time = 10880;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ReceiveTime = 10881;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Message = 10883;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Severity = 10884;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConditionClassId = 11608;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConditionClassName = 11609;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConditionName = 11572;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_BranchId = 10885;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Retain = 10886;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EnabledState = 10887;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EnabledState_Id = 10888;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Quality = 10893;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Quality_SourceTimestamp = 10894;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_LastSeverity = 10897;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_LastSeverity_SourceTimestamp = 10898;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Comment = 10899;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Comment_SourceTimestamp = 10900;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ClientUserId = 10901;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AddComment_InputArguments = 10905;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AckedState = 10908;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AckedState_Id = 10909;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConfirmedState_Id = 10917;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Acknowledge_InputArguments = 10925;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Confirm_InputArguments = 10927;

        /// <remarks />
        public const uint Data_Dynamic_Array_BooleanValue = 10928;

        /// <remarks />
        public const uint Data_Dynamic_Array_SByteValue = 10929;

        /// <remarks />
        public const uint Data_Dynamic_Array_ByteValue = 10930;

        /// <remarks />
        public const uint Data_Dynamic_Array_Int16Value = 10931;

        /// <remarks />
        public const uint Data_Dynamic_Array_UInt16Value = 10932;

        /// <remarks />
        public const uint Data_Dynamic_Array_Int32Value = 10933;

        /// <remarks />
        public const uint Data_Dynamic_Array_UInt32Value = 10934;

        /// <remarks />
        public const uint Data_Dynamic_Array_Int64Value = 10935;

        /// <remarks />
        public const uint Data_Dynamic_Array_UInt64Value = 10936;

        /// <remarks />
        public const uint Data_Dynamic_Array_FloatValue = 10937;

        /// <remarks />
        public const uint Data_Dynamic_Array_DoubleValue = 10938;

        /// <remarks />
        public const uint Data_Dynamic_Array_StringValue = 10939;

        /// <remarks />
        public const uint Data_Dynamic_Array_DateTimeValue = 10940;

        /// <remarks />
        public const uint Data_Dynamic_Array_GuidValue = 10941;

        /// <remarks />
        public const uint Data_Dynamic_Array_ByteStringValue = 10942;

        /// <remarks />
        public const uint Data_Dynamic_Array_XmlElementValue = 10943;

        /// <remarks />
        public const uint Data_Dynamic_Array_NodeIdValue = 10944;

        /// <remarks />
        public const uint Data_Dynamic_Array_ExpandedNodeIdValue = 10945;

        /// <remarks />
        public const uint Data_Dynamic_Array_QualifiedNameValue = 10946;

        /// <remarks />
        public const uint Data_Dynamic_Array_LocalizedTextValue = 10947;

        /// <remarks />
        public const uint Data_Dynamic_Array_StatusCodeValue = 10948;

        /// <remarks />
        public const uint Data_Dynamic_Array_VariantValue = 10949;

        /// <remarks />
        public const uint Data_Dynamic_Array_EnumerationValue = 10950;

        /// <remarks />
        public const uint Data_Dynamic_Array_StructureValue = 10951;

        /// <remarks />
        public const uint Data_Dynamic_Array_NumberValue = 10952;

        /// <remarks />
        public const uint Data_Dynamic_Array_IntegerValue = 10953;

        /// <remarks />
        public const uint Data_Dynamic_Array_UIntegerValue = 10954;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_SimulationActive = 10956;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_GenerateValues_InputArguments = 10958;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EventId = 10960;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EventType = 10961;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_SourceNode = 10962;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_SourceName = 10963;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Time = 10964;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ReceiveTime = 10965;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Message = 10967;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Severity = 10968;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionClassId = 11610;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionClassName = 11611;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionName = 11573;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_BranchId = 10969;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Retain = 10970;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EnabledState = 10971;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EnabledState_Id = 10972;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Quality = 10977;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Quality_SourceTimestamp = 10978;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_LastSeverity = 10981;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = 10982;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Comment = 10983;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Comment_SourceTimestamp = 10984;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ClientUserId = 10985;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AddComment_InputArguments = 10989;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AckedState = 10992;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AckedState_Id = 10993;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConfirmedState_Id = 11001;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Acknowledge_InputArguments = 11009;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Confirm_InputArguments = 11011;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_BooleanValue = 11012;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_SByteValue = 11013;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_ByteValue = 11014;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_Int16Value = 11015;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_UInt16Value = 11016;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_Int32Value = 11017;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_UInt32Value = 11018;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_Int64Value = 11019;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_UInt64Value = 11020;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_FloatValue = 11021;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_DoubleValue = 11022;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_StringValue = 11023;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_DateTimeValue = 11024;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_GuidValue = 11025;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_ByteStringValue = 11026;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_XmlElementValue = 11027;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_NodeIdValue = 11028;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_ExpandedNodeIdValue = 11029;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_QualifiedNameValue = 11030;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_LocalizedTextValue = 11031;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_StatusCodeValue = 11032;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_VariantValue = 11033;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_SimulationActive = 11035;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_GenerateValues_InputArguments = 11037;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EventId = 11039;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EventType = 11040;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_SourceNode = 11041;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_SourceName = 11042;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Time = 11043;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ReceiveTime = 11044;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Message = 11046;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Severity = 11047;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionClassId = 11612;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionClassName = 11613;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionName = 11574;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_BranchId = 11048;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Retain = 11049;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EnabledState = 11050;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EnabledState_Id = 11051;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Quality = 11056;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Quality_SourceTimestamp = 11057;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_LastSeverity = 11060;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_LastSeverity_SourceTimestamp = 11061;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Comment = 11062;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Comment_SourceTimestamp = 11063;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ClientUserId = 11064;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AddComment_InputArguments = 11068;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AckedState = 11071;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AckedState_Id = 11072;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConfirmedState_Id = 11080;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Acknowledge_InputArguments = 11088;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Confirm_InputArguments = 11090;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_BooleanValue = 11091;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_SByteValue = 11092;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_ByteValue = 11093;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_Int16Value = 11094;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_UInt16Value = 11095;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_Int32Value = 11096;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_UInt32Value = 11097;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_Int64Value = 11098;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_UInt64Value = 11099;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_FloatValue = 11100;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_DoubleValue = 11101;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_StringValue = 11102;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_DateTimeValue = 11103;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_GuidValue = 11104;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_ByteStringValue = 11105;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_XmlElementValue = 11106;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_NodeIdValue = 11107;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_ExpandedNodeIdValue = 11108;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_QualifiedNameValue = 11109;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_LocalizedTextValue = 11110;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_StatusCodeValue = 11111;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_VariantValue = 11112;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_SimulationActive = 11114;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_GenerateValues_InputArguments = 11116;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EventId = 11118;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EventType = 11119;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_SourceNode = 11120;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_SourceName = 11121;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Time = 11122;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ReceiveTime = 11123;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Message = 11125;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Severity = 11126;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassId = 11614;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassName = 11615;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionName = 11575;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_BranchId = 11127;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Retain = 11128;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EnabledState = 11129;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EnabledState_Id = 11130;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Quality = 11135;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Quality_SourceTimestamp = 11136;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity = 11139;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = 11140;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Comment = 11141;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Comment_SourceTimestamp = 11142;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ClientUserId = 11143;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AddComment_InputArguments = 11147;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AckedState = 11150;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AckedState_Id = 11151;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConfirmedState_Id = 11159;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge_InputArguments = 11167;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Confirm_InputArguments = 11169;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_SByteValue = 11170;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_SByteValue_EURange = 11173;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_ByteValue = 11176;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_ByteValue_EURange = 11179;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int16Value = 11182;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int16Value_EURange = 11185;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt16Value = 11188;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt16Value_EURange = 11191;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int32Value = 11194;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int32Value_EURange = 11197;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt32Value = 11200;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt32Value_EURange = 11203;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int64Value = 11206;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int64Value_EURange = 11209;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt64Value = 11212;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt64Value_EURange = 11215;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_FloatValue = 11218;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_FloatValue_EURange = 11221;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_DoubleValue = 11224;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_DoubleValue_EURange = 11227;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_NumberValue = 11230;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_NumberValue_EURange = 11233;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_IntegerValue = 11236;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_IntegerValue_EURange = 11239;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UIntegerValue = 11242;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UIntegerValue_EURange = 11245;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_SimulationActive = 11249;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_GenerateValues_InputArguments = 11251;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EventId = 11253;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EventType = 11254;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_SourceNode = 11255;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_SourceName = 11256;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Time = 11257;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ReceiveTime = 11258;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Message = 11260;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Severity = 11261;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionClassId = 11616;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionClassName = 11617;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionName = 11576;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_BranchId = 11262;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Retain = 11263;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EnabledState = 11264;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EnabledState_Id = 11265;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Quality = 11270;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Quality_SourceTimestamp = 11271;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_LastSeverity = 11274;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = 11275;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Comment = 11276;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Comment_SourceTimestamp = 11277;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ClientUserId = 11278;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AddComment_InputArguments = 11282;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AckedState = 11285;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AckedState_Id = 11286;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConfirmedState_Id = 11294;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Acknowledge_InputArguments = 11302;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Confirm_InputArguments = 11304;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_SByteValue = 11305;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_SByteValue_EURange = 11308;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_ByteValue = 11311;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_ByteValue_EURange = 11314;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int16Value = 11317;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int16Value_EURange = 11320;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt16Value = 11323;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt16Value_EURange = 11326;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int32Value = 11329;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int32Value_EURange = 11332;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt32Value = 11335;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt32Value_EURange = 11338;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int64Value = 11341;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int64Value_EURange = 11344;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt64Value = 11347;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt64Value_EURange = 11350;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_FloatValue = 11353;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_FloatValue_EURange = 11356;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_DoubleValue = 11359;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_DoubleValue_EURange = 11362;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_NumberValue = 11365;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_NumberValue_EURange = 11368;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_IntegerValue = 11371;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_IntegerValue_EURange = 11374;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UIntegerValue = 11377;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UIntegerValue_EURange = 11380;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EventId = 11385;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EventType = 11386;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_SourceNode = 11387;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_SourceName = 11388;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Time = 11389;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ReceiveTime = 11390;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Message = 11392;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Severity = 11393;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ConditionClassId = 11618;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ConditionClassName = 11619;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ConditionName = 11577;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_BranchId = 11394;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Retain = 11395;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EnabledState = 11396;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EnabledState_Id = 11397;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Quality = 11402;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Quality_SourceTimestamp = 11403;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_LastSeverity = 11406;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_LastSeverity_SourceTimestamp = 11407;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Comment = 11408;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Comment_SourceTimestamp = 11409;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ClientUserId = 11410;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_AddComment_InputArguments = 11414;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_MonitoredNodeCount = 11417;

        /// <remarks />
        public const uint TestData_BinarySchema = 11422;

        /// <remarks />
        public const uint TestData_BinarySchema_NamespaceUri = 11424;

        /// <remarks />
        public const uint TestData_BinarySchema_Deprecated = 15045;

        /// <remarks />
        public const uint TestData_BinarySchema_ScalarValueDataType = 11425;

        /// <remarks />
        public const uint TestData_BinarySchema_ArrayValueDataType = 11428;

        /// <remarks />
        public const uint TestData_BinarySchema_UserScalarValueDataType = 11431;

        /// <remarks />
        public const uint TestData_BinarySchema_UserArrayValueDataType = 11434;

        /// <remarks />
        public const uint TestData_BinarySchema_Vector = 21015;

        /// <remarks />
        public const uint TestData_BinarySchema_WorkOrderStatusType = 1024;

        /// <remarks />
        public const uint TestData_BinarySchema_WorkOrderType = 1027;

        /// <remarks />
        public const uint TestData_XmlSchema = 11441;

        /// <remarks />
        public const uint TestData_XmlSchema_NamespaceUri = 11443;

        /// <remarks />
        public const uint TestData_XmlSchema_Deprecated = 15046;

        /// <remarks />
        public const uint TestData_XmlSchema_ScalarValueDataType = 11444;

        /// <remarks />
        public const uint TestData_XmlSchema_ArrayValueDataType = 11447;

        /// <remarks />
        public const uint TestData_XmlSchema_UserScalarValueDataType = 11450;

        /// <remarks />
        public const uint TestData_XmlSchema_UserArrayValueDataType = 11453;

        /// <remarks />
        public const uint TestData_XmlSchema_Vector = 1043;

        /// <remarks />
        public const uint TestData_XmlSchema_WorkOrderStatusType = 1052;

        /// <remarks />
        public const uint TestData_XmlSchema_WorkOrderType = 1055;
    }
    #endregion

    #region VariableType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableTypes
    {
        /// <remarks />
        public const uint TestDataVariableType = 1001;

        /// <remarks />
        public const uint ScalarValueVariableType = 1002;
    }
    #endregion

    #region DataType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueDataType = new ExpandedNodeId(TestData.DataTypes.ScalarValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueDataType = new ExpandedNodeId(TestData.DataTypes.ArrayValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId BooleanDataType = new ExpandedNodeId(TestData.DataTypes.BooleanDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId SByteDataType = new ExpandedNodeId(TestData.DataTypes.SByteDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ByteDataType = new ExpandedNodeId(TestData.DataTypes.ByteDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Int16DataType = new ExpandedNodeId(TestData.DataTypes.Int16DataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UInt16DataType = new ExpandedNodeId(TestData.DataTypes.UInt16DataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Int32DataType = new ExpandedNodeId(TestData.DataTypes.Int32DataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UInt32DataType = new ExpandedNodeId(TestData.DataTypes.UInt32DataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Int64DataType = new ExpandedNodeId(TestData.DataTypes.Int64DataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UInt64DataType = new ExpandedNodeId(TestData.DataTypes.UInt64DataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId FloatDataType = new ExpandedNodeId(TestData.DataTypes.FloatDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId DoubleDataType = new ExpandedNodeId(TestData.DataTypes.DoubleDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StringDataType = new ExpandedNodeId(TestData.DataTypes.StringDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId DateTimeDataType = new ExpandedNodeId(TestData.DataTypes.DateTimeDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId GuidDataType = new ExpandedNodeId(TestData.DataTypes.GuidDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ByteStringDataType = new ExpandedNodeId(TestData.DataTypes.ByteStringDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId XmlElementDataType = new ExpandedNodeId(TestData.DataTypes.XmlElementDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId NodeIdDataType = new ExpandedNodeId(TestData.DataTypes.NodeIdDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ExpandedNodeIdDataType = new ExpandedNodeId(TestData.DataTypes.ExpandedNodeIdDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId QualifiedNameDataType = new ExpandedNodeId(TestData.DataTypes.QualifiedNameDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId LocalizedTextDataType = new ExpandedNodeId(TestData.DataTypes.LocalizedTextDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StatusCodeDataType = new ExpandedNodeId(TestData.DataTypes.StatusCodeDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId VariantDataType = new ExpandedNodeId(TestData.DataTypes.VariantDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueDataType = new ExpandedNodeId(TestData.DataTypes.UserScalarValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueDataType = new ExpandedNodeId(TestData.DataTypes.UserArrayValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Vector = new ExpandedNodeId(TestData.DataTypes.Vector, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderStatusType = new ExpandedNodeId(TestData.DataTypes.WorkOrderStatusType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderType = new ExpandedNodeId(TestData.DataTypes.WorkOrderType, TestData.Namespaces.TestData);
    }
    #endregion

    #region Method Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class MethodIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_GenerateValues = new ExpandedNodeId(TestData.Methods.TestDataObjectType_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.TestDataObjectType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.TestDataObjectType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.TestDataObjectType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.TestDataObjectType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_GenerateValues = new ExpandedNodeId(TestData.Methods.TestDataVariableType_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.TestDataVariableType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.TestDataVariableType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.TestDataVariableType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.TestDataVariableType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.ScalarValueVariableType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.ScalarValueVariableType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.ScalarValueVariableType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.ScalarValueVariableType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.ScalarValueObjectType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.ScalarValueObjectType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.ScalarValueObjectType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.ScalarValueObjectType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.AnalogScalarValueObjectType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.AnalogScalarValueObjectType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.AnalogScalarValueObjectType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.AnalogScalarValueObjectType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.ArrayValueObjectType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.ArrayValueObjectType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.ArrayValueObjectType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.ArrayValueObjectType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.AnalogArrayValueObjectType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.AnalogArrayValueObjectType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.AnalogArrayValueObjectType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.AnalogArrayValueObjectType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.UserScalarValueObjectType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.UserScalarValueObjectType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.UserScalarValueObjectType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.UserScalarValueObjectType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.UserArrayValueObjectType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.UserArrayValueObjectType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.UserArrayValueObjectType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.UserArrayValueObjectType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod1 = new ExpandedNodeId(TestData.Methods.MethodTestType_ScalarMethod1, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod2 = new ExpandedNodeId(TestData.Methods.MethodTestType_ScalarMethod2, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod3 = new ExpandedNodeId(TestData.Methods.MethodTestType_ScalarMethod3, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod1 = new ExpandedNodeId(TestData.Methods.MethodTestType_ArrayMethod1, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod2 = new ExpandedNodeId(TestData.Methods.MethodTestType_ArrayMethod2, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod3 = new ExpandedNodeId(TestData.Methods.MethodTestType_ArrayMethod3, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod1 = new ExpandedNodeId(TestData.Methods.MethodTestType_UserScalarMethod1, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod2 = new ExpandedNodeId(TestData.Methods.MethodTestType_UserScalarMethod2, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod1 = new ExpandedNodeId(TestData.Methods.MethodTestType_UserArrayMethod1, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod2 = new ExpandedNodeId(TestData.Methods.MethodTestType_UserArrayMethod2, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_Scalar_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_Scalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_Scalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_Scalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_Scalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_StructureScalar_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_StructureScalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_StructureScalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_StructureScalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_StructureScalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_Array_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_Array_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_Array_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_Array_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_Array_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_UserScalar_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_UserScalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_UserScalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_UserScalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_UserScalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_UserArray_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_UserArray_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_UserArray_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_UserArray_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_UserArray_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogScalar_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogScalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogScalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogScalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogScalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogArray_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogArray_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogArray_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogArray_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogArray_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod1 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ScalarMethod1, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod2 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ScalarMethod2, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod3 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ScalarMethod3, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod1 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ArrayMethod1, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod2 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ArrayMethod2, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod3 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ArrayMethod3, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod1 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_UserScalarMethod1, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod2 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_UserScalarMethod2, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod1 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_UserArrayMethod1, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod2 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_UserArrayMethod2, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Scalar_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Scalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Scalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Scalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Scalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_StructureScalar_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_StructureScalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_StructureScalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_StructureScalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_StructureScalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Array_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Array_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Array_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Array_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Array_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserScalar_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserScalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserScalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserScalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserScalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserArray_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserArray_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserArray_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserArray_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserArray_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogScalar_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogScalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogScalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogScalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogArray_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogArray_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogArray_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogArray_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogArray_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Disable = new ExpandedNodeId(TestData.Methods.Data_Conditions_SystemStatus_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Enable = new ExpandedNodeId(TestData.Methods.Data_Conditions_SystemStatus_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_AddComment = new ExpandedNodeId(TestData.Methods.Data_Conditions_SystemStatus_AddComment, TestData.Namespaces.TestData);
    }
    #endregion

    #region Object Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete = new ExpandedNodeId(TestData.Objects.TestDataObjectType_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete = new ExpandedNodeId(TestData.Objects.TestDataVariableType_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data = new ExpandedNodeId(TestData.Objects.Data, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static = new ExpandedNodeId(TestData.Objects.Data_Static, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar = new ExpandedNodeId(TestData.Objects.Data_Static_Scalar, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_Scalar_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_StructureScalar_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array = new ExpandedNodeId(TestData.Objects.Data_Static_Array, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_Array_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar = new ExpandedNodeId(TestData.Objects.Data_Static_UserScalar, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_UserScalar_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray = new ExpandedNodeId(TestData.Objects.Data_Static_UserArray, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_UserArray_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar = new ExpandedNodeId(TestData.Objects.Data_Static_AnalogScalar, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_AnalogScalar_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray = new ExpandedNodeId(TestData.Objects.Data_Static_AnalogArray, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_AnalogArray_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest = new ExpandedNodeId(TestData.Objects.Data_Static_MethodTest, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic = new ExpandedNodeId(TestData.Objects.Data_Dynamic, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar = new ExpandedNodeId(TestData.Objects.Data_Dynamic_Scalar, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_Scalar_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_StructureScalar_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array = new ExpandedNodeId(TestData.Objects.Data_Dynamic_Array, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_Array_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar = new ExpandedNodeId(TestData.Objects.Data_Dynamic_UserScalar, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_UserScalar_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray = new ExpandedNodeId(TestData.Objects.Data_Dynamic_UserArray, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_UserArray_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar = new ExpandedNodeId(TestData.Objects.Data_Dynamic_AnalogScalar, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_AnalogScalar_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray = new ExpandedNodeId(TestData.Objects.Data_Dynamic_AnalogArray, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_AnalogArray_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions = new ExpandedNodeId(TestData.Objects.Data_Conditions, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus = new ExpandedNodeId(TestData.Objects.Data_Conditions_SystemStatus, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.ScalarValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.ArrayValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.UserScalarValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.UserArrayValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Vector_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.Vector_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderStatusType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.WorkOrderStatusType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.WorkOrderType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.ScalarValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.ArrayValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.UserScalarValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.UserArrayValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Vector_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.Vector_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderStatusType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.WorkOrderStatusType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.WorkOrderType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueDataType_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.ScalarValueDataType_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueDataType_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.ArrayValueDataType_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueDataType_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.UserScalarValueDataType_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueDataType_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.UserArrayValueDataType_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Vector_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.Vector_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderStatusType_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.WorkOrderStatusType_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderType_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.WorkOrderType_Encoding_DefaultJson, TestData.Namespaces.TestData);
    }
    #endregion

    #region ObjectType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId GenerateValuesEventType = new ExpandedNodeId(TestData.ObjectTypes.GenerateValuesEventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType = new ExpandedNodeId(TestData.ObjectTypes.TestDataObjectType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.ScalarValueObjectType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.AnalogScalarValueObjectType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.ArrayValueObjectType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.AnalogArrayValueObjectType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.UserScalarValueObjectType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.UserArrayValueObjectType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType = new ExpandedNodeId(TestData.ObjectTypes.MethodTestType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestSystemConditionType = new ExpandedNodeId(TestData.ObjectTypes.TestSystemConditionType, TestData.Namespaces.TestData);
    }
    #endregion

    #region Variable Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId GenerateValuesEventType_Iterations = new ExpandedNodeId(TestData.Variables.GenerateValuesEventType_Iterations, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId GenerateValuesEventType_NewValueCount = new ExpandedNodeId(TestData.Variables.GenerateValuesEventType_NewValueCount, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_SimulationActive = new ExpandedNodeId(TestData.Variables.TestDataObjectType_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataObjectType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_SimulationActive = new ExpandedNodeId(TestData.Variables.TestDataVariableType_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataVariableType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataVariableType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_BooleanValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_SByteValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_ByteValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_Int16Value = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_UInt16Value = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_Int32Value = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_UInt32Value = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_Int64Value = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_UInt64Value = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_FloatValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_DoubleValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_StringValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_DateTimeValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_GuidValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_ByteStringValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_XmlElementValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_NodeIdValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_StatusCodeValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_VariantValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_EnumerationValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_StructureValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_NumberValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_IntegerValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType_UIntegerValue = new ExpandedNodeId(TestData.Variables.ScalarValueVariableType_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_BooleanValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_StringValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_DateTimeValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_GuidValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_ByteStringValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_XmlElementValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_NodeIdValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_StatusCodeValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_VariantValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_EnumerationValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_StructureValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_NumberValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_IntegerValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_UIntegerValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_NumberValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_IntegerValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UIntegerValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_BooleanValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_StringValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_DateTimeValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_GuidValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_ByteStringValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_XmlElementValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_NodeIdValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_StatusCodeValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_VariantValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_EnumerationValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_StructureValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_NumberValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_IntegerValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_UIntegerValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_NumberValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_IntegerValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UIntegerValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_BooleanValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_StringValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_DateTimeValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_GuidValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_ByteStringValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_XmlElementValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_NodeIdValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_StatusCodeValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueObjectType_VariantValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_BooleanValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_StringValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_DateTimeValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_GuidValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_ByteStringValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_XmlElementValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_NodeIdValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_StatusCodeValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueObjectType_VariantValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod3_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod3_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod3_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod3_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod3_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod3_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod3_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod3_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserScalarMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserScalarMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserScalarMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserScalarMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserArrayMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserArrayMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserArrayMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserArrayMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestSystemConditionType_EnabledState_Id = new ExpandedNodeId(TestData.Variables.TestSystemConditionType_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestSystemConditionType_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.TestSystemConditionType_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestSystemConditionType_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.TestSystemConditionType_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestSystemConditionType_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.TestSystemConditionType_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestSystemConditionType_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.TestSystemConditionType_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestSystemConditionType_ConditionRefresh_InputArguments = new ExpandedNodeId(TestData.Variables.TestSystemConditionType_ConditionRefresh_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestSystemConditionType_ConditionRefresh2_InputArguments = new ExpandedNodeId(TestData.Variables.TestSystemConditionType_ConditionRefresh2_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestSystemConditionType_MonitoredNodeCount = new ExpandedNodeId(TestData.Variables.TestSystemConditionType_MonitoredNodeCount, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_StringValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_StringValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_StructureScalar_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_StructureScalar_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_Array_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Array_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_StringValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_StringValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserScalar_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_StringValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_UserArray_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod3_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod3_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod3_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod3_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod3_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod3_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod3_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod3_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserScalarMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserScalarMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserScalarMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserScalarMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserArrayMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserArrayMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserArrayMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserArrayMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_StringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_StringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_StructureScalar_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_StructureScalar_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_StringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_StringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_StringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_EventId = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_EventType = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_SourceName = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Time = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Message = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Severity = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_BranchId = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Retain = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Quality = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Comment = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_MonitoredNodeCount = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_MonitoredNodeCount, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_NamespaceUri = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_NamespaceUri, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_Deprecated = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_Deprecated, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_ScalarValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_ScalarValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_ArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_ArrayValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_UserScalarValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_UserScalarValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_UserArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_UserArrayValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_Vector = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_Vector, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_WorkOrderStatusType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_WorkOrderStatusType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_WorkOrderType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_WorkOrderType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_NamespaceUri = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_NamespaceUri, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_Deprecated = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_Deprecated, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_ScalarValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_ScalarValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_ArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_ArrayValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_UserScalarValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_UserScalarValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_UserArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_UserArrayValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_Vector = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_Vector, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_WorkOrderStatusType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_WorkOrderStatusType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_WorkOrderType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_WorkOrderType, TestData.Namespaces.TestData);
    }
    #endregion

    #region VariableType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId TestDataVariableType = new ExpandedNodeId(TestData.VariableTypes.TestDataVariableType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueVariableType = new ExpandedNodeId(TestData.VariableTypes.ScalarValueVariableType, TestData.Namespaces.TestData);
    }
    #endregion

    #region BrowseName Declarations
    /// <remarks />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class BrowseNames
    {
        /// <remarks />
        public const string AnalogArrayValueObjectType = "AnalogArrayValueObjectType";

        /// <remarks />
        public const string AnalogScalarValueObjectType = "AnalogScalarValueObjectType";

        /// <remarks />
        public const string ArrayMethod1 = "ArrayMethod1";

        /// <remarks />
        public const string ArrayMethod2 = "ArrayMethod2";

        /// <remarks />
        public const string ArrayMethod3 = "ArrayMethod3";

        /// <remarks />
        public const string ArrayValueDataType = "ArrayValueDataType";

        /// <remarks />
        public const string ArrayValueObjectType = "ArrayValueObjectType";

        /// <remarks />
        public const string BooleanDataType = "BooleanDataType";

        /// <remarks />
        public const string BooleanValue = "BooleanValue";

        /// <remarks />
        public const string ByteDataType = "ByteDataType";

        /// <remarks />
        public const string ByteStringDataType = "ByteStringDataType";

        /// <remarks />
        public const string ByteStringValue = "ByteStringValue";

        /// <remarks />
        public const string ByteValue = "ByteValue";

        /// <remarks />
        public const string Conditions = "Conditions";

        /// <remarks />
        public const string CycleComplete = "CycleComplete";

        /// <remarks />
        public const string Data = "Data";

        /// <remarks />
        public const string DateTimeDataType = "DateTimeDataType";

        /// <remarks />
        public const string DateTimeValue = "DateTimeValue";

        /// <remarks />
        public const string DoubleDataType = "DoubleDataType";

        /// <remarks />
        public const string DoubleValue = "DoubleValue";

        /// <remarks />
        public const string Dynamic = "Dynamic";

        /// <remarks />
        public const string EnumerationValue = "EnumerationValue";

        /// <remarks />
        public const string ExpandedNodeIdDataType = "ExpandedNodeIdDataType";

        /// <remarks />
        public const string ExpandedNodeIdValue = "ExpandedNodeIdValue";

        /// <remarks />
        public const string FloatDataType = "FloatDataType";

        /// <remarks />
        public const string FloatValue = "FloatValue";

        /// <remarks />
        public const string GenerateValues = "GenerateValues";

        /// <remarks />
        public const string GenerateValuesEventType = "GenerateValuesEventType";

        /// <remarks />
        public const string GuidDataType = "GuidDataType";

        /// <remarks />
        public const string GuidValue = "GuidValue";

        /// <remarks />
        public const string Int16DataType = "Int16DataType";

        /// <remarks />
        public const string Int16Value = "Int16Value";

        /// <remarks />
        public const string Int32DataType = "Int32DataType";

        /// <remarks />
        public const string Int32Value = "Int32Value";

        /// <remarks />
        public const string Int64DataType = "Int64DataType";

        /// <remarks />
        public const string Int64Value = "Int64Value";

        /// <remarks />
        public const string IntegerValue = "IntegerValue";

        /// <remarks />
        public const string Iterations = "Iterations";

        /// <remarks />
        public const string LocalizedTextDataType = "LocalizedTextDataType";

        /// <remarks />
        public const string LocalizedTextValue = "LocalizedTextValue";

        /// <remarks />
        public const string MethodTestType = "MethodTestType";

        /// <remarks />
        public const string MonitoredNodeCount = "MonitoredNodeCount";

        /// <remarks />
        public const string NewValueCount = "NewValueCount";

        /// <remarks />
        public const string NodeIdDataType = "NodeIdDataType";

        /// <remarks />
        public const string NodeIdValue = "NodeIdValue";

        /// <remarks />
        public const string NumberValue = "NumberValue";

        /// <remarks />
        public const string QualifiedNameDataType = "QualifiedNameDataType";

        /// <remarks />
        public const string QualifiedNameValue = "QualifiedNameValue";

        /// <remarks />
        public const string SByteDataType = "SByteDataType";

        /// <remarks />
        public const string SByteValue = "SByteValue";

        /// <remarks />
        public const string ScalarMethod1 = "ScalarMethod1";

        /// <remarks />
        public const string ScalarMethod2 = "ScalarMethod2";

        /// <remarks />
        public const string ScalarMethod3 = "ScalarMethod3";

        /// <remarks />
        public const string ScalarValueDataType = "ScalarValueDataType";

        /// <remarks />
        public const string ScalarValueObjectType = "ScalarValueObjectType";

        /// <remarks />
        public const string ScalarValueVariableType = "ScalarValueVariableType";

        /// <remarks />
        public const string SimulationActive = "SimulationActive";

        /// <remarks />
        public const string Static = "Static";

        /// <remarks />
        public const string StatusCodeDataType = "StatusCodeDataType";

        /// <remarks />
        public const string StatusCodeValue = "StatusCodeValue";

        /// <remarks />
        public const string StringDataType = "StringDataType";

        /// <remarks />
        public const string StringValue = "StringValue";

        /// <remarks />
        public const string StructureValue = "StructureValue";

        /// <remarks />
        public const string TestData_BinarySchema = "TestData";

        /// <remarks />
        public const string TestData_XmlSchema = "TestData";

        /// <remarks />
        public const string TestDataObjectType = "TestDataObjectType";

        /// <remarks />
        public const string TestDataVariableType = "TestDataVariableType";

        /// <remarks />
        public const string TestSystemConditionType = "TestSystemConditionType";

        /// <remarks />
        public const string UInt16DataType = "UInt16DataType";

        /// <remarks />
        public const string UInt16Value = "UInt16Value";

        /// <remarks />
        public const string UInt32DataType = "UInt32DataType";

        /// <remarks />
        public const string UInt32Value = "UInt32Value";

        /// <remarks />
        public const string UInt64DataType = "UInt64DataType";

        /// <remarks />
        public const string UInt64Value = "UInt64Value";

        /// <remarks />
        public const string UIntegerValue = "UIntegerValue";

        /// <remarks />
        public const string UserArrayMethod1 = "UserArrayMethod1";

        /// <remarks />
        public const string UserArrayMethod2 = "UserArrayMethod2";

        /// <remarks />
        public const string UserArrayValueDataType = "UserArrayValueDataType";

        /// <remarks />
        public const string UserArrayValueObjectType = "UserArrayValueObjectType";

        /// <remarks />
        public const string UserScalarMethod1 = "UserScalarMethod1";

        /// <remarks />
        public const string UserScalarMethod2 = "UserScalarMethod2";

        /// <remarks />
        public const string UserScalarValueDataType = "UserScalarValueDataType";

        /// <remarks />
        public const string UserScalarValueObjectType = "UserScalarValueObjectType";

        /// <remarks />
        public const string VariantDataType = "VariantDataType";

        /// <remarks />
        public const string VariantValue = "VariantValue";

        /// <remarks />
        public const string Vector = "Vector";

        /// <remarks />
        public const string WorkOrderStatusType = "WorkOrderStatusType";

        /// <remarks />
        public const string WorkOrderType = "WorkOrderType";

        /// <remarks />
        public const string XmlElementDataType = "XmlElementDataType";

        /// <remarks />
        public const string XmlElementValue = "XmlElementValue";
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
        /// The URI for the TestData namespace (.NET code namespace is 'TestData').
        /// </summary>
        public const string TestData = "http://test.org/UA/Data/";
    }
    #endregion
}