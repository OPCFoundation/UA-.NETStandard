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
        public const uint ScalarValueDataType = 1078;

        /// <remarks />
        public const uint ArrayValueDataType = 1473;

        /// <remarks />
        public const uint BooleanDataType = 1715;

        /// <remarks />
        public const uint SByteDataType = 1716;

        /// <remarks />
        public const uint ByteDataType = 1717;

        /// <remarks />
        public const uint Int16DataType = 1718;

        /// <remarks />
        public const uint UInt16DataType = 1719;

        /// <remarks />
        public const uint Int32DataType = 1720;

        /// <remarks />
        public const uint UInt32DataType = 1721;

        /// <remarks />
        public const uint Int64DataType = 1722;

        /// <remarks />
        public const uint UInt64DataType = 1723;

        /// <remarks />
        public const uint FloatDataType = 1724;

        /// <remarks />
        public const uint DoubleDataType = 1725;

        /// <remarks />
        public const uint StringDataType = 1726;

        /// <remarks />
        public const uint DateTimeDataType = 1727;

        /// <remarks />
        public const uint GuidDataType = 1728;

        /// <remarks />
        public const uint ByteStringDataType = 1729;

        /// <remarks />
        public const uint XmlElementDataType = 1730;

        /// <remarks />
        public const uint NodeIdDataType = 1731;

        /// <remarks />
        public const uint ExpandedNodeIdDataType = 1732;

        /// <remarks />
        public const uint QualifiedNameDataType = 1733;

        /// <remarks />
        public const uint LocalizedTextDataType = 1734;

        /// <remarks />
        public const uint StatusCodeDataType = 1735;

        /// <remarks />
        public const uint VariantDataType = 1736;

        /// <remarks />
        public const uint UserScalarValueDataType = 1737;

        /// <remarks />
        public const uint UserArrayValueDataType = 1829;

        /// <remarks />
        public const uint Vector = 1915;

        /// <remarks />
        public const uint WorkOrderStatusType = 1920;

        /// <remarks />
        public const uint WorkOrderType = 1921;
    }
    #endregion

    #region Method Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Methods
    {
        /// <remarks />
        public const uint TestDataObjectType_GenerateValues = 1017;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Disable = 1052;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Enable = 1053;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_AddComment = 1054;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Acknowledge = 1074;

        /// <remarks />
        public const uint TestDataVariableType_GenerateValues = 1081;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Disable = 1116;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Enable = 1117;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_AddComment = 1118;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Acknowledge = 1138;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Disable = 1179;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Enable = 1180;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_AddComment = 1181;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Acknowledge = 1201;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Disable = 1278;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Enable = 1279;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AddComment = 1280;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Acknowledge = 1300;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Disable = 1369;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Enable = 1370;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AddComment = 1371;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Acknowledge = 1391;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Disable = 1520;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Enable = 1521;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AddComment = 1522;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Acknowledge = 1542;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Disable = 1611;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Enable = 1612;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AddComment = 1613;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Acknowledge = 1633;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Disable = 1775;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Enable = 1776;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AddComment = 1777;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Acknowledge = 1797;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Disable = 1867;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Enable = 1868;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AddComment = 1869;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Acknowledge = 1889;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod1 = 1929;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod2 = 1932;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod3 = 1935;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod1 = 1938;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod2 = 1941;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod3 = 1944;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod1 = 1947;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod2 = 1950;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod1 = 1953;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod2 = 1956;

        /// <remarks />
        public const uint Data_Static_Scalar_GenerateValues = 2005;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Disable = 2040;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Enable = 2041;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AddComment = 2042;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Acknowledge = 2062;

        /// <remarks />
        public const uint Data_Static_StructureScalar_GenerateValues = 2096;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Disable = 2131;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Enable = 2132;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_AddComment = 2133;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Acknowledge = 2153;

        /// <remarks />
        public const uint Data_Static_Array_GenerateValues = 2186;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Disable = 2221;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Enable = 2222;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AddComment = 2223;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Acknowledge = 2243;

        /// <remarks />
        public const uint Data_Static_UserScalar_GenerateValues = 2277;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Disable = 2312;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Enable = 2313;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AddComment = 2314;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Acknowledge = 2334;

        /// <remarks />
        public const uint Data_Static_UserArray_GenerateValues = 2362;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Disable = 2397;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Enable = 2398;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AddComment = 2399;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Acknowledge = 2419;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_GenerateValues = 2447;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Disable = 2482;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Enable = 2483;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AddComment = 2484;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Acknowledge = 2504;

        /// <remarks />
        public const uint Data_Static_AnalogArray_GenerateValues = 2588;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Disable = 2623;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Enable = 2624;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AddComment = 2625;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Acknowledge = 2645;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod1 = 2728;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod2 = 2731;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod3 = 2734;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod1 = 2737;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod2 = 2740;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod3 = 2743;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod1 = 2746;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod2 = 2749;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod1 = 2752;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod2 = 2755;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_GenerateValues = 2761;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Disable = 2796;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Enable = 2797;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AddComment = 2798;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Acknowledge = 2818;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_GenerateValues = 2852;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Disable = 2887;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Enable = 2888;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_AddComment = 2889;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Acknowledge = 2909;

        /// <remarks />
        public const uint Data_Dynamic_Array_GenerateValues = 2942;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Disable = 2977;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Enable = 2978;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AddComment = 2979;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Acknowledge = 2999;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_GenerateValues = 3033;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Disable = 3068;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Enable = 3069;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AddComment = 3070;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Acknowledge = 3090;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_GenerateValues = 3118;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Disable = 3153;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Enable = 3154;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AddComment = 3155;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Acknowledge = 3175;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_GenerateValues = 3203;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Disable = 3238;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Enable = 3239;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AddComment = 3240;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge = 3260;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_GenerateValues = 3344;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Disable = 3379;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Enable = 3380;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AddComment = 3381;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Acknowledge = 3401;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Disable = 3517;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Enable = 3518;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_AddComment = 3519;
    }
    #endregion

    #region Object Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Objects
    {
        /// <remarks />
        public const uint TestDataObjectType_CycleComplete = 1019;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete = 1083;

        /// <remarks />
        public const uint Data = 2001;

        /// <remarks />
        public const uint Data_Static = 2002;

        /// <remarks />
        public const uint Data_Static_Scalar = 2003;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete = 2007;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete = 2098;

        /// <remarks />
        public const uint Data_Static_Array = 2184;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete = 2188;

        /// <remarks />
        public const uint Data_Static_UserScalar = 2275;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete = 2279;

        /// <remarks />
        public const uint Data_Static_UserArray = 2360;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete = 2364;

        /// <remarks />
        public const uint Data_Static_AnalogScalar = 2445;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete = 2449;

        /// <remarks />
        public const uint Data_Static_AnalogArray = 2586;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete = 2590;

        /// <remarks />
        public const uint Data_Static_MethodTest = 2727;

        /// <remarks />
        public const uint Data_Dynamic = 2758;

        /// <remarks />
        public const uint Data_Dynamic_Scalar = 2759;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete = 2763;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete = 2854;

        /// <remarks />
        public const uint Data_Dynamic_Array = 2940;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete = 2944;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar = 3031;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete = 3035;

        /// <remarks />
        public const uint Data_Dynamic_UserArray = 3116;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete = 3120;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar = 3201;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete = 3205;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray = 3342;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete = 3346;

        /// <remarks />
        public const uint Data_Conditions = 3483;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus = 3484;

        /// <remarks />
        public const uint ScalarValueDataType_Encoding_DefaultBinary = 3522;

        /// <remarks />
        public const uint ArrayValueDataType_Encoding_DefaultBinary = 3523;

        /// <remarks />
        public const uint UserScalarValueDataType_Encoding_DefaultBinary = 3524;

        /// <remarks />
        public const uint UserArrayValueDataType_Encoding_DefaultBinary = 3525;

        /// <remarks />
        public const uint Vector_Encoding_DefaultBinary = 3526;

        /// <remarks />
        public const uint WorkOrderStatusType_Encoding_DefaultBinary = 3527;

        /// <remarks />
        public const uint WorkOrderType_Encoding_DefaultBinary = 3528;

        /// <remarks />
        public const uint ScalarValueDataType_Encoding_DefaultXml = 3554;

        /// <remarks />
        public const uint ArrayValueDataType_Encoding_DefaultXml = 3555;

        /// <remarks />
        public const uint UserScalarValueDataType_Encoding_DefaultXml = 3556;

        /// <remarks />
        public const uint UserArrayValueDataType_Encoding_DefaultXml = 3557;

        /// <remarks />
        public const uint Vector_Encoding_DefaultXml = 3558;

        /// <remarks />
        public const uint WorkOrderStatusType_Encoding_DefaultXml = 3559;

        /// <remarks />
        public const uint WorkOrderType_Encoding_DefaultXml = 3560;

        /// <remarks />
        public const uint ScalarValueDataType_Encoding_DefaultJson = 3586;

        /// <remarks />
        public const uint ArrayValueDataType_Encoding_DefaultJson = 3587;

        /// <remarks />
        public const uint UserScalarValueDataType_Encoding_DefaultJson = 3588;

        /// <remarks />
        public const uint UserArrayValueDataType_Encoding_DefaultJson = 3589;

        /// <remarks />
        public const uint Vector_Encoding_DefaultJson = 3590;

        /// <remarks />
        public const uint WorkOrderStatusType_Encoding_DefaultJson = 3591;

        /// <remarks />
        public const uint WorkOrderType_Encoding_DefaultJson = 3592;
    }
    #endregion

    #region ObjectType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class ObjectTypes
    {
        /// <remarks />
        public const uint GenerateValuesEventType = 1003;

        /// <remarks />
        public const uint TestDataObjectType = 1015;

        /// <remarks />
        public const uint ScalarValueObjectType = 1241;

        /// <remarks />
        public const uint AnalogScalarValueObjectType = 1332;

        /// <remarks />
        public const uint ArrayValueObjectType = 1483;

        /// <remarks />
        public const uint AnalogArrayValueObjectType = 1574;

        /// <remarks />
        public const uint UserScalarValueObjectType = 1738;

        /// <remarks />
        public const uint UserArrayValueObjectType = 1830;

        /// <remarks />
        public const uint MethodTestType = 1928;

        /// <remarks />
        public const uint TestSystemConditionType = 1959;
    }
    #endregion

    #region Variable Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Variables
    {
        /// <remarks />
        public const uint GenerateValuesEventType_Iterations = 1013;

        /// <remarks />
        public const uint GenerateValuesEventType_NewValueCount = 1014;

        /// <remarks />
        public const uint TestDataObjectType_SimulationActive = 1016;

        /// <remarks />
        public const uint TestDataObjectType_GenerateValues_InputArguments = 1018;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_EventId = 1020;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_EventType = 1021;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_SourceNode = 1022;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_SourceName = 1023;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Time = 1024;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ReceiveTime = 1025;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Message = 1027;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Severity = 1028;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ConditionClassId = 1029;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ConditionClassName = 1030;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ConditionName = 1033;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_BranchId = 1034;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Retain = 1035;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_EnabledState = 1036;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_EnabledState_Id = 1037;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Quality = 1045;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Quality_SourceTimestamp = 1046;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_LastSeverity = 1047;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1048;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Comment = 1049;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Comment_SourceTimestamp = 1050;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ClientUserId = 1051;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_AddComment_InputArguments = 1055;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_AckedState = 1056;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_AckedState_Id = 1057;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_ConfirmedState_Id = 1066;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Acknowledge_InputArguments = 1075;

        /// <remarks />
        public const uint TestDataObjectType_CycleComplete_Confirm_InputArguments = 1077;

        /// <remarks />
        public const uint TestDataVariableType_SimulationActive = 1080;

        /// <remarks />
        public const uint TestDataVariableType_GenerateValues_InputArguments = 1082;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_EventId = 1084;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_EventType = 1085;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_SourceNode = 1086;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_SourceName = 1087;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Time = 1088;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ReceiveTime = 1089;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Message = 1091;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Severity = 1092;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ConditionClassId = 1093;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ConditionClassName = 1094;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ConditionName = 1097;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_BranchId = 1098;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Retain = 1099;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_EnabledState = 1100;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_EnabledState_Id = 1101;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Quality = 1109;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Quality_SourceTimestamp = 1110;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_LastSeverity = 1111;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_LastSeverity_SourceTimestamp = 1112;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Comment = 1113;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Comment_SourceTimestamp = 1114;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ClientUserId = 1115;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_AddComment_InputArguments = 1119;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_AckedState = 1120;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_AckedState_Id = 1121;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_ConfirmedState_Id = 1130;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Acknowledge_InputArguments = 1139;

        /// <remarks />
        public const uint TestDataVariableType_CycleComplete_Confirm_InputArguments = 1141;

        /// <remarks />
        public const uint ScalarValueVariableType_GenerateValues_InputArguments = 1145;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_EventId = 1147;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_EventType = 1148;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_SourceNode = 1149;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_SourceName = 1150;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Time = 1151;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ReceiveTime = 1152;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Message = 1154;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Severity = 1155;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ConditionClassId = 1156;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ConditionClassName = 1157;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ConditionName = 1160;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_BranchId = 1161;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Retain = 1162;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_EnabledState = 1163;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_EnabledState_Id = 1164;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Quality = 1172;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Quality_SourceTimestamp = 1173;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_LastSeverity = 1174;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_LastSeverity_SourceTimestamp = 1175;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Comment = 1176;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Comment_SourceTimestamp = 1177;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ClientUserId = 1178;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_AddComment_InputArguments = 1182;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_AckedState = 1183;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_AckedState_Id = 1184;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_ConfirmedState_Id = 1193;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Acknowledge_InputArguments = 1202;

        /// <remarks />
        public const uint ScalarValueVariableType_CycleComplete_Confirm_InputArguments = 1204;

        /// <remarks />
        public const uint ScalarValueVariableType_BooleanValue = 1205;

        /// <remarks />
        public const uint ScalarValueVariableType_SByteValue = 1206;

        /// <remarks />
        public const uint ScalarValueVariableType_ByteValue = 1207;

        /// <remarks />
        public const uint ScalarValueVariableType_Int16Value = 1208;

        /// <remarks />
        public const uint ScalarValueVariableType_UInt16Value = 1209;

        /// <remarks />
        public const uint ScalarValueVariableType_Int32Value = 1210;

        /// <remarks />
        public const uint ScalarValueVariableType_UInt32Value = 1211;

        /// <remarks />
        public const uint ScalarValueVariableType_Int64Value = 1212;

        /// <remarks />
        public const uint ScalarValueVariableType_UInt64Value = 1213;

        /// <remarks />
        public const uint ScalarValueVariableType_FloatValue = 1214;

        /// <remarks />
        public const uint ScalarValueVariableType_DoubleValue = 1215;

        /// <remarks />
        public const uint ScalarValueVariableType_StringValue = 1216;

        /// <remarks />
        public const uint ScalarValueVariableType_DateTimeValue = 1217;

        /// <remarks />
        public const uint ScalarValueVariableType_GuidValue = 1218;

        /// <remarks />
        public const uint ScalarValueVariableType_ByteStringValue = 1219;

        /// <remarks />
        public const uint ScalarValueVariableType_XmlElementValue = 1220;

        /// <remarks />
        public const uint ScalarValueVariableType_NodeIdValue = 1221;

        /// <remarks />
        public const uint ScalarValueVariableType_ExpandedNodeIdValue = 1222;

        /// <remarks />
        public const uint ScalarValueVariableType_QualifiedNameValue = 1223;

        /// <remarks />
        public const uint ScalarValueVariableType_LocalizedTextValue = 1224;

        /// <remarks />
        public const uint ScalarValueVariableType_StatusCodeValue = 1225;

        /// <remarks />
        public const uint ScalarValueVariableType_VariantValue = 1226;

        /// <remarks />
        public const uint ScalarValueVariableType_EnumerationValue = 1227;

        /// <remarks />
        public const uint ScalarValueVariableType_StructureValue = 1228;

        /// <remarks />
        public const uint ScalarValueVariableType_NumberValue = 1229;

        /// <remarks />
        public const uint ScalarValueVariableType_IntegerValue = 1230;

        /// <remarks />
        public const uint ScalarValueVariableType_UIntegerValue = 1231;

        /// <remarks />
        public const uint ScalarValueObjectType_GenerateValues_InputArguments = 1244;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EventId = 1246;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EventType = 1247;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_SourceNode = 1248;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_SourceName = 1249;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Time = 1250;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ReceiveTime = 1251;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Message = 1253;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Severity = 1254;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConditionClassId = 1255;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConditionClassName = 1256;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConditionName = 1259;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_BranchId = 1260;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Retain = 1261;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EnabledState = 1262;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EnabledState_Id = 1263;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Quality = 1271;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = 1272;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_LastSeverity = 1273;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1274;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Comment = 1275;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = 1276;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ClientUserId = 1277;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AddComment_InputArguments = 1281;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AckedState = 1282;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AckedState_Id = 1283;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConfirmedState_Id = 1292;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = 1301;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Confirm_InputArguments = 1303;

        /// <remarks />
        public const uint ScalarValueObjectType_BooleanValue = 1304;

        /// <remarks />
        public const uint ScalarValueObjectType_SByteValue = 1305;

        /// <remarks />
        public const uint ScalarValueObjectType_ByteValue = 1306;

        /// <remarks />
        public const uint ScalarValueObjectType_Int16Value = 1307;

        /// <remarks />
        public const uint ScalarValueObjectType_UInt16Value = 1308;

        /// <remarks />
        public const uint ScalarValueObjectType_Int32Value = 1309;

        /// <remarks />
        public const uint ScalarValueObjectType_UInt32Value = 1310;

        /// <remarks />
        public const uint ScalarValueObjectType_Int64Value = 1311;

        /// <remarks />
        public const uint ScalarValueObjectType_UInt64Value = 1312;

        /// <remarks />
        public const uint ScalarValueObjectType_FloatValue = 1313;

        /// <remarks />
        public const uint ScalarValueObjectType_DoubleValue = 1314;

        /// <remarks />
        public const uint ScalarValueObjectType_StringValue = 1315;

        /// <remarks />
        public const uint ScalarValueObjectType_DateTimeValue = 1316;

        /// <remarks />
        public const uint ScalarValueObjectType_GuidValue = 1317;

        /// <remarks />
        public const uint ScalarValueObjectType_ByteStringValue = 1318;

        /// <remarks />
        public const uint ScalarValueObjectType_XmlElementValue = 1319;

        /// <remarks />
        public const uint ScalarValueObjectType_NodeIdValue = 1320;

        /// <remarks />
        public const uint ScalarValueObjectType_ExpandedNodeIdValue = 1321;

        /// <remarks />
        public const uint ScalarValueObjectType_QualifiedNameValue = 1322;

        /// <remarks />
        public const uint ScalarValueObjectType_LocalizedTextValue = 1323;

        /// <remarks />
        public const uint ScalarValueObjectType_StatusCodeValue = 1324;

        /// <remarks />
        public const uint ScalarValueObjectType_VariantValue = 1325;

        /// <remarks />
        public const uint ScalarValueObjectType_EnumerationValue = 1326;

        /// <remarks />
        public const uint ScalarValueObjectType_StructureValue = 1327;

        /// <remarks />
        public const uint ScalarValueObjectType_NumberValue = 1328;

        /// <remarks />
        public const uint ScalarValueObjectType_IntegerValue = 1329;

        /// <remarks />
        public const uint ScalarValueObjectType_UIntegerValue = 1330;

        /// <remarks />
        public const uint ScalarValueObjectType_VectorValue = 1331;

        /// <remarks />
        public const uint ScalarValueObjectType_VectorValue_X = 3593;

        /// <remarks />
        public const uint ScalarValueObjectType_VectorValue_Y = 3594;

        /// <remarks />
        public const uint ScalarValueObjectType_VectorValue_Z = 3595;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_GenerateValues_InputArguments = 1335;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EventId = 1337;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EventType = 1338;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_SourceNode = 1339;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_SourceName = 1340;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Time = 1341;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ReceiveTime = 1342;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Message = 1344;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Severity = 1345;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConditionClassId = 1346;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConditionClassName = 1347;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConditionName = 1350;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_BranchId = 1351;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Retain = 1352;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EnabledState = 1353;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EnabledState_Id = 1354;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Quality = 1362;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = 1363;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_LastSeverity = 1364;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1365;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Comment = 1366;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = 1367;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ClientUserId = 1368;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AddComment_InputArguments = 1372;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AckedState = 1373;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AckedState_Id = 1374;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConfirmedState_Id = 1383;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = 1392;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Confirm_InputArguments = 1394;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_SByteValue = 1395;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_SByteValue_EURange = 1399;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_ByteValue = 1401;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_ByteValue_EURange = 1405;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int16Value = 1407;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int16Value_EURange = 1411;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt16Value = 1413;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt16Value_EURange = 1417;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int32Value = 1419;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int32Value_EURange = 1423;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt32Value = 1425;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt32Value_EURange = 1429;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int64Value = 1431;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int64Value_EURange = 1435;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt64Value = 1437;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt64Value_EURange = 1441;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_FloatValue = 1443;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_FloatValue_EURange = 1447;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_DoubleValue = 1449;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_DoubleValue_EURange = 1453;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_NumberValue = 1455;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_NumberValue_EURange = 1459;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_IntegerValue = 1461;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_IntegerValue_EURange = 1465;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UIntegerValue = 1467;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UIntegerValue_EURange = 1471;

        /// <remarks />
        public const uint ArrayValueObjectType_GenerateValues_InputArguments = 1486;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EventId = 1488;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EventType = 1489;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_SourceNode = 1490;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_SourceName = 1491;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Time = 1492;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ReceiveTime = 1493;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Message = 1495;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Severity = 1496;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConditionClassId = 1497;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConditionClassName = 1498;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConditionName = 1501;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_BranchId = 1502;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Retain = 1503;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EnabledState = 1504;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EnabledState_Id = 1505;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Quality = 1513;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = 1514;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_LastSeverity = 1515;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1516;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Comment = 1517;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = 1518;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ClientUserId = 1519;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AddComment_InputArguments = 1523;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AckedState = 1524;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AckedState_Id = 1525;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConfirmedState_Id = 1534;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = 1543;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Confirm_InputArguments = 1545;

        /// <remarks />
        public const uint ArrayValueObjectType_BooleanValue = 1546;

        /// <remarks />
        public const uint ArrayValueObjectType_SByteValue = 1547;

        /// <remarks />
        public const uint ArrayValueObjectType_ByteValue = 1548;

        /// <remarks />
        public const uint ArrayValueObjectType_Int16Value = 1549;

        /// <remarks />
        public const uint ArrayValueObjectType_UInt16Value = 1550;

        /// <remarks />
        public const uint ArrayValueObjectType_Int32Value = 1551;

        /// <remarks />
        public const uint ArrayValueObjectType_UInt32Value = 1552;

        /// <remarks />
        public const uint ArrayValueObjectType_Int64Value = 1553;

        /// <remarks />
        public const uint ArrayValueObjectType_UInt64Value = 1554;

        /// <remarks />
        public const uint ArrayValueObjectType_FloatValue = 1555;

        /// <remarks />
        public const uint ArrayValueObjectType_DoubleValue = 1556;

        /// <remarks />
        public const uint ArrayValueObjectType_StringValue = 1557;

        /// <remarks />
        public const uint ArrayValueObjectType_DateTimeValue = 1558;

        /// <remarks />
        public const uint ArrayValueObjectType_GuidValue = 1559;

        /// <remarks />
        public const uint ArrayValueObjectType_ByteStringValue = 1560;

        /// <remarks />
        public const uint ArrayValueObjectType_XmlElementValue = 1561;

        /// <remarks />
        public const uint ArrayValueObjectType_NodeIdValue = 1562;

        /// <remarks />
        public const uint ArrayValueObjectType_ExpandedNodeIdValue = 1563;

        /// <remarks />
        public const uint ArrayValueObjectType_QualifiedNameValue = 1564;

        /// <remarks />
        public const uint ArrayValueObjectType_LocalizedTextValue = 1565;

        /// <remarks />
        public const uint ArrayValueObjectType_StatusCodeValue = 1566;

        /// <remarks />
        public const uint ArrayValueObjectType_VariantValue = 1567;

        /// <remarks />
        public const uint ArrayValueObjectType_EnumerationValue = 1568;

        /// <remarks />
        public const uint ArrayValueObjectType_StructureValue = 1569;

        /// <remarks />
        public const uint ArrayValueObjectType_NumberValue = 1570;

        /// <remarks />
        public const uint ArrayValueObjectType_IntegerValue = 1571;

        /// <remarks />
        public const uint ArrayValueObjectType_UIntegerValue = 1572;

        /// <remarks />
        public const uint ArrayValueObjectType_VectorValue = 1573;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_GenerateValues_InputArguments = 1577;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EventId = 1579;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EventType = 1580;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_SourceNode = 1581;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_SourceName = 1582;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Time = 1583;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ReceiveTime = 1584;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Message = 1586;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Severity = 1587;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConditionClassId = 1588;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConditionClassName = 1589;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConditionName = 1592;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_BranchId = 1593;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Retain = 1594;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EnabledState = 1595;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EnabledState_Id = 1596;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Quality = 1604;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = 1605;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_LastSeverity = 1606;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1607;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Comment = 1608;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = 1609;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ClientUserId = 1610;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AddComment_InputArguments = 1614;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AckedState = 1615;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AckedState_Id = 1616;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConfirmedState_Id = 1625;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = 1634;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Confirm_InputArguments = 1636;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_SByteValue = 1637;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_SByteValue_EURange = 1641;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_ByteValue = 1643;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_ByteValue_EURange = 1647;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int16Value = 1649;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int16Value_EURange = 1653;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt16Value = 1655;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt16Value_EURange = 1659;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int32Value = 1661;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int32Value_EURange = 1665;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt32Value = 1667;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt32Value_EURange = 1671;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int64Value = 1673;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int64Value_EURange = 1677;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt64Value = 1679;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt64Value_EURange = 1683;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_FloatValue = 1685;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_FloatValue_EURange = 1689;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_DoubleValue = 1691;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_DoubleValue_EURange = 1695;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_NumberValue = 1697;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_NumberValue_EURange = 1701;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_IntegerValue = 1703;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_IntegerValue_EURange = 1707;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UIntegerValue = 1709;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UIntegerValue_EURange = 1713;

        /// <remarks />
        public const uint UserScalarValueObjectType_GenerateValues_InputArguments = 1741;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EventId = 1743;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EventType = 1744;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_SourceNode = 1745;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_SourceName = 1746;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Time = 1747;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ReceiveTime = 1748;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Message = 1750;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Severity = 1751;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConditionClassId = 1752;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConditionClassName = 1753;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConditionName = 1756;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_BranchId = 1757;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Retain = 1758;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EnabledState = 1759;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EnabledState_Id = 1760;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Quality = 1768;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = 1769;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_LastSeverity = 1770;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1771;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Comment = 1772;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = 1773;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ClientUserId = 1774;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AddComment_InputArguments = 1778;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AckedState = 1779;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AckedState_Id = 1780;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConfirmedState_Id = 1789;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = 1798;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Confirm_InputArguments = 1800;

        /// <remarks />
        public const uint UserScalarValueObjectType_BooleanValue = 1801;

        /// <remarks />
        public const uint UserScalarValueObjectType_SByteValue = 1802;

        /// <remarks />
        public const uint UserScalarValueObjectType_ByteValue = 1803;

        /// <remarks />
        public const uint UserScalarValueObjectType_Int16Value = 1804;

        /// <remarks />
        public const uint UserScalarValueObjectType_UInt16Value = 1805;

        /// <remarks />
        public const uint UserScalarValueObjectType_Int32Value = 1806;

        /// <remarks />
        public const uint UserScalarValueObjectType_UInt32Value = 1807;

        /// <remarks />
        public const uint UserScalarValueObjectType_Int64Value = 1808;

        /// <remarks />
        public const uint UserScalarValueObjectType_UInt64Value = 1809;

        /// <remarks />
        public const uint UserScalarValueObjectType_FloatValue = 1810;

        /// <remarks />
        public const uint UserScalarValueObjectType_DoubleValue = 1811;

        /// <remarks />
        public const uint UserScalarValueObjectType_StringValue = 1812;

        /// <remarks />
        public const uint UserScalarValueObjectType_DateTimeValue = 1813;

        /// <remarks />
        public const uint UserScalarValueObjectType_GuidValue = 1814;

        /// <remarks />
        public const uint UserScalarValueObjectType_ByteStringValue = 1815;

        /// <remarks />
        public const uint UserScalarValueObjectType_XmlElementValue = 1816;

        /// <remarks />
        public const uint UserScalarValueObjectType_NodeIdValue = 1817;

        /// <remarks />
        public const uint UserScalarValueObjectType_ExpandedNodeIdValue = 1818;

        /// <remarks />
        public const uint UserScalarValueObjectType_QualifiedNameValue = 1819;

        /// <remarks />
        public const uint UserScalarValueObjectType_LocalizedTextValue = 1820;

        /// <remarks />
        public const uint UserScalarValueObjectType_StatusCodeValue = 1821;

        /// <remarks />
        public const uint UserScalarValueObjectType_VariantValue = 1822;

        /// <remarks />
        public const uint UserArrayValueObjectType_GenerateValues_InputArguments = 1833;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EventId = 1835;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EventType = 1836;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_SourceNode = 1837;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_SourceName = 1838;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Time = 1839;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ReceiveTime = 1840;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Message = 1842;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Severity = 1843;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConditionClassId = 1844;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConditionClassName = 1845;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConditionName = 1848;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_BranchId = 1849;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Retain = 1850;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EnabledState = 1851;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EnabledState_Id = 1852;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Quality = 1860;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = 1861;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_LastSeverity = 1862;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1863;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Comment = 1864;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = 1865;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ClientUserId = 1866;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AddComment_InputArguments = 1870;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AckedState = 1871;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AckedState_Id = 1872;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConfirmedState_Id = 1881;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = 1890;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Confirm_InputArguments = 1892;

        /// <remarks />
        public const uint UserArrayValueObjectType_BooleanValue = 1893;

        /// <remarks />
        public const uint UserArrayValueObjectType_SByteValue = 1894;

        /// <remarks />
        public const uint UserArrayValueObjectType_ByteValue = 1895;

        /// <remarks />
        public const uint UserArrayValueObjectType_Int16Value = 1896;

        /// <remarks />
        public const uint UserArrayValueObjectType_UInt16Value = 1897;

        /// <remarks />
        public const uint UserArrayValueObjectType_Int32Value = 1898;

        /// <remarks />
        public const uint UserArrayValueObjectType_UInt32Value = 1899;

        /// <remarks />
        public const uint UserArrayValueObjectType_Int64Value = 1900;

        /// <remarks />
        public const uint UserArrayValueObjectType_UInt64Value = 1901;

        /// <remarks />
        public const uint UserArrayValueObjectType_FloatValue = 1902;

        /// <remarks />
        public const uint UserArrayValueObjectType_DoubleValue = 1903;

        /// <remarks />
        public const uint UserArrayValueObjectType_StringValue = 1904;

        /// <remarks />
        public const uint UserArrayValueObjectType_DateTimeValue = 1905;

        /// <remarks />
        public const uint UserArrayValueObjectType_GuidValue = 1906;

        /// <remarks />
        public const uint UserArrayValueObjectType_ByteStringValue = 1907;

        /// <remarks />
        public const uint UserArrayValueObjectType_XmlElementValue = 1908;

        /// <remarks />
        public const uint UserArrayValueObjectType_NodeIdValue = 1909;

        /// <remarks />
        public const uint UserArrayValueObjectType_ExpandedNodeIdValue = 1910;

        /// <remarks />
        public const uint UserArrayValueObjectType_QualifiedNameValue = 1911;

        /// <remarks />
        public const uint UserArrayValueObjectType_LocalizedTextValue = 1912;

        /// <remarks />
        public const uint UserArrayValueObjectType_StatusCodeValue = 1913;

        /// <remarks />
        public const uint UserArrayValueObjectType_VariantValue = 1914;

        /// <remarks />
        public const uint VectorVariableType_X = 1917;

        /// <remarks />
        public const uint VectorVariableType_Y = 1918;

        /// <remarks />
        public const uint VectorVariableType_Z = 1919;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod1_InputArguments = 1930;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod1_OutputArguments = 1931;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod2_InputArguments = 1933;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod2_OutputArguments = 1934;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod3_InputArguments = 1936;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod3_OutputArguments = 1937;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod1_InputArguments = 1939;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod1_OutputArguments = 1940;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod2_InputArguments = 1942;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod2_OutputArguments = 1943;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod3_InputArguments = 1945;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod3_OutputArguments = 1946;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod1_InputArguments = 1948;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod1_OutputArguments = 1949;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod2_InputArguments = 1951;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod2_OutputArguments = 1952;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod1_InputArguments = 1954;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod1_OutputArguments = 1955;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod2_InputArguments = 1957;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod2_OutputArguments = 1958;

        /// <remarks />
        public const uint TestSystemConditionType_EnabledState_Id = 1977;

        /// <remarks />
        public const uint TestSystemConditionType_Quality_SourceTimestamp = 1986;

        /// <remarks />
        public const uint TestSystemConditionType_LastSeverity_SourceTimestamp = 1988;

        /// <remarks />
        public const uint TestSystemConditionType_Comment_SourceTimestamp = 1990;

        /// <remarks />
        public const uint TestSystemConditionType_AddComment_InputArguments = 1995;

        /// <remarks />
        public const uint TestSystemConditionType_ConditionRefresh_InputArguments = 1997;

        /// <remarks />
        public const uint TestSystemConditionType_ConditionRefresh2_InputArguments = 1999;

        /// <remarks />
        public const uint TestSystemConditionType_MonitoredNodeCount = 2000;

        /// <remarks />
        public const uint Data_Static_Scalar_SimulationActive = 2004;

        /// <remarks />
        public const uint Data_Static_Scalar_GenerateValues_InputArguments = 2006;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EventId = 2008;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EventType = 2009;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_SourceNode = 2010;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_SourceName = 2011;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Time = 2012;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ReceiveTime = 2013;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Message = 2015;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Severity = 2016;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConditionClassId = 2017;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConditionClassName = 2018;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConditionName = 2021;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_BranchId = 2022;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Retain = 2023;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EnabledState = 2024;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EnabledState_Id = 2025;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Quality = 2033;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Quality_SourceTimestamp = 2034;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_LastSeverity = 2035;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_LastSeverity_SourceTimestamp = 2036;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Comment = 2037;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Comment_SourceTimestamp = 2038;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ClientUserId = 2039;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AddComment_InputArguments = 2043;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AckedState = 2044;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AckedState_Id = 2045;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConfirmedState_Id = 2054;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Acknowledge_InputArguments = 2063;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Confirm_InputArguments = 2065;

        /// <remarks />
        public const uint Data_Static_Scalar_BooleanValue = 2066;

        /// <remarks />
        public const uint Data_Static_Scalar_SByteValue = 2067;

        /// <remarks />
        public const uint Data_Static_Scalar_ByteValue = 2068;

        /// <remarks />
        public const uint Data_Static_Scalar_Int16Value = 2069;

        /// <remarks />
        public const uint Data_Static_Scalar_UInt16Value = 2070;

        /// <remarks />
        public const uint Data_Static_Scalar_Int32Value = 2071;

        /// <remarks />
        public const uint Data_Static_Scalar_UInt32Value = 2072;

        /// <remarks />
        public const uint Data_Static_Scalar_Int64Value = 2073;

        /// <remarks />
        public const uint Data_Static_Scalar_UInt64Value = 2074;

        /// <remarks />
        public const uint Data_Static_Scalar_FloatValue = 2075;

        /// <remarks />
        public const uint Data_Static_Scalar_DoubleValue = 2076;

        /// <remarks />
        public const uint Data_Static_Scalar_StringValue = 2077;

        /// <remarks />
        public const uint Data_Static_Scalar_DateTimeValue = 2078;

        /// <remarks />
        public const uint Data_Static_Scalar_GuidValue = 2079;

        /// <remarks />
        public const uint Data_Static_Scalar_ByteStringValue = 2080;

        /// <remarks />
        public const uint Data_Static_Scalar_XmlElementValue = 2081;

        /// <remarks />
        public const uint Data_Static_Scalar_NodeIdValue = 2082;

        /// <remarks />
        public const uint Data_Static_Scalar_ExpandedNodeIdValue = 2083;

        /// <remarks />
        public const uint Data_Static_Scalar_QualifiedNameValue = 2084;

        /// <remarks />
        public const uint Data_Static_Scalar_LocalizedTextValue = 2085;

        /// <remarks />
        public const uint Data_Static_Scalar_StatusCodeValue = 2086;

        /// <remarks />
        public const uint Data_Static_Scalar_VariantValue = 2087;

        /// <remarks />
        public const uint Data_Static_Scalar_EnumerationValue = 2088;

        /// <remarks />
        public const uint Data_Static_Scalar_StructureValue = 2089;

        /// <remarks />
        public const uint Data_Static_Scalar_NumberValue = 2090;

        /// <remarks />
        public const uint Data_Static_Scalar_IntegerValue = 2091;

        /// <remarks />
        public const uint Data_Static_Scalar_UIntegerValue = 2092;

        /// <remarks />
        public const uint Data_Static_Scalar_VectorValue = 2093;

        /// <remarks />
        public const uint Data_Static_Scalar_VectorValue_X = 3596;

        /// <remarks />
        public const uint Data_Static_Scalar_VectorValue_Y = 3597;

        /// <remarks />
        public const uint Data_Static_Scalar_VectorValue_Z = 3598;

        /// <remarks />
        public const uint Data_Static_StructureScalar = 2094;

        /// <remarks />
        public const uint Data_Static_StructureScalar_SimulationActive = 2095;

        /// <remarks />
        public const uint Data_Static_StructureScalar_GenerateValues_InputArguments = 2097;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_EventId = 2099;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_EventType = 2100;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_SourceNode = 2101;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_SourceName = 2102;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Time = 2103;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ReceiveTime = 2104;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Message = 2106;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Severity = 2107;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ConditionClassId = 2108;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ConditionClassName = 2109;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ConditionName = 2112;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_BranchId = 2113;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Retain = 2114;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_EnabledState = 2115;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_EnabledState_Id = 2116;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Quality = 2124;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Quality_SourceTimestamp = 2125;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_LastSeverity = 2126;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_LastSeverity_SourceTimestamp = 2127;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Comment = 2128;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Comment_SourceTimestamp = 2129;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ClientUserId = 2130;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_AddComment_InputArguments = 2134;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_AckedState = 2135;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_AckedState_Id = 2136;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_ConfirmedState_Id = 2145;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Acknowledge_InputArguments = 2154;

        /// <remarks />
        public const uint Data_Static_StructureScalar_CycleComplete_Confirm_InputArguments = 2156;

        /// <remarks />
        public const uint Data_Static_StructureScalar_BooleanValue = 2157;

        /// <remarks />
        public const uint Data_Static_StructureScalar_SByteValue = 2158;

        /// <remarks />
        public const uint Data_Static_StructureScalar_ByteValue = 2159;

        /// <remarks />
        public const uint Data_Static_StructureScalar_Int16Value = 2160;

        /// <remarks />
        public const uint Data_Static_StructureScalar_UInt16Value = 2161;

        /// <remarks />
        public const uint Data_Static_StructureScalar_Int32Value = 2162;

        /// <remarks />
        public const uint Data_Static_StructureScalar_UInt32Value = 2163;

        /// <remarks />
        public const uint Data_Static_StructureScalar_Int64Value = 2164;

        /// <remarks />
        public const uint Data_Static_StructureScalar_UInt64Value = 2165;

        /// <remarks />
        public const uint Data_Static_StructureScalar_FloatValue = 2166;

        /// <remarks />
        public const uint Data_Static_StructureScalar_DoubleValue = 2167;

        /// <remarks />
        public const uint Data_Static_StructureScalar_StringValue = 2168;

        /// <remarks />
        public const uint Data_Static_StructureScalar_DateTimeValue = 2169;

        /// <remarks />
        public const uint Data_Static_StructureScalar_GuidValue = 2170;

        /// <remarks />
        public const uint Data_Static_StructureScalar_ByteStringValue = 2171;

        /// <remarks />
        public const uint Data_Static_StructureScalar_XmlElementValue = 2172;

        /// <remarks />
        public const uint Data_Static_StructureScalar_NodeIdValue = 2173;

        /// <remarks />
        public const uint Data_Static_StructureScalar_ExpandedNodeIdValue = 2174;

        /// <remarks />
        public const uint Data_Static_StructureScalar_QualifiedNameValue = 2175;

        /// <remarks />
        public const uint Data_Static_StructureScalar_LocalizedTextValue = 2176;

        /// <remarks />
        public const uint Data_Static_StructureScalar_StatusCodeValue = 2177;

        /// <remarks />
        public const uint Data_Static_StructureScalar_VariantValue = 2178;

        /// <remarks />
        public const uint Data_Static_StructureScalar_EnumerationValue = 2179;

        /// <remarks />
        public const uint Data_Static_StructureScalar_StructureValue = 2180;

        /// <remarks />
        public const uint Data_Static_StructureScalar_NumberValue = 2181;

        /// <remarks />
        public const uint Data_Static_StructureScalar_IntegerValue = 2182;

        /// <remarks />
        public const uint Data_Static_StructureScalar_UIntegerValue = 2183;

        /// <remarks />
        public const uint Data_Static_Array_SimulationActive = 2185;

        /// <remarks />
        public const uint Data_Static_Array_GenerateValues_InputArguments = 2187;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EventId = 2189;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EventType = 2190;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_SourceNode = 2191;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_SourceName = 2192;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Time = 2193;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ReceiveTime = 2194;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Message = 2196;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Severity = 2197;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConditionClassId = 2198;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConditionClassName = 2199;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConditionName = 2202;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_BranchId = 2203;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Retain = 2204;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EnabledState = 2205;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EnabledState_Id = 2206;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Quality = 2214;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Quality_SourceTimestamp = 2215;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_LastSeverity = 2216;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_LastSeverity_SourceTimestamp = 2217;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Comment = 2218;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Comment_SourceTimestamp = 2219;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ClientUserId = 2220;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AddComment_InputArguments = 2224;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AckedState = 2225;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AckedState_Id = 2226;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConfirmedState_Id = 2235;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Acknowledge_InputArguments = 2244;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Confirm_InputArguments = 2246;

        /// <remarks />
        public const uint Data_Static_Array_BooleanValue = 2247;

        /// <remarks />
        public const uint Data_Static_Array_SByteValue = 2248;

        /// <remarks />
        public const uint Data_Static_Array_ByteValue = 2249;

        /// <remarks />
        public const uint Data_Static_Array_Int16Value = 2250;

        /// <remarks />
        public const uint Data_Static_Array_UInt16Value = 2251;

        /// <remarks />
        public const uint Data_Static_Array_Int32Value = 2252;

        /// <remarks />
        public const uint Data_Static_Array_UInt32Value = 2253;

        /// <remarks />
        public const uint Data_Static_Array_Int64Value = 2254;

        /// <remarks />
        public const uint Data_Static_Array_UInt64Value = 2255;

        /// <remarks />
        public const uint Data_Static_Array_FloatValue = 2256;

        /// <remarks />
        public const uint Data_Static_Array_DoubleValue = 2257;

        /// <remarks />
        public const uint Data_Static_Array_StringValue = 2258;

        /// <remarks />
        public const uint Data_Static_Array_DateTimeValue = 2259;

        /// <remarks />
        public const uint Data_Static_Array_GuidValue = 2260;

        /// <remarks />
        public const uint Data_Static_Array_ByteStringValue = 2261;

        /// <remarks />
        public const uint Data_Static_Array_XmlElementValue = 2262;

        /// <remarks />
        public const uint Data_Static_Array_NodeIdValue = 2263;

        /// <remarks />
        public const uint Data_Static_Array_ExpandedNodeIdValue = 2264;

        /// <remarks />
        public const uint Data_Static_Array_QualifiedNameValue = 2265;

        /// <remarks />
        public const uint Data_Static_Array_LocalizedTextValue = 2266;

        /// <remarks />
        public const uint Data_Static_Array_StatusCodeValue = 2267;

        /// <remarks />
        public const uint Data_Static_Array_VariantValue = 2268;

        /// <remarks />
        public const uint Data_Static_Array_EnumerationValue = 2269;

        /// <remarks />
        public const uint Data_Static_Array_StructureValue = 2270;

        /// <remarks />
        public const uint Data_Static_Array_NumberValue = 2271;

        /// <remarks />
        public const uint Data_Static_Array_IntegerValue = 2272;

        /// <remarks />
        public const uint Data_Static_Array_UIntegerValue = 2273;

        /// <remarks />
        public const uint Data_Static_Array_VectorValue = 2274;

        /// <remarks />
        public const uint Data_Static_UserScalar_SimulationActive = 2276;

        /// <remarks />
        public const uint Data_Static_UserScalar_GenerateValues_InputArguments = 2278;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EventId = 2280;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EventType = 2281;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_SourceNode = 2282;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_SourceName = 2283;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Time = 2284;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ReceiveTime = 2285;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Message = 2287;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Severity = 2288;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConditionClassId = 2289;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConditionClassName = 2290;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConditionName = 2293;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_BranchId = 2294;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Retain = 2295;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EnabledState = 2296;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EnabledState_Id = 2297;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Quality = 2305;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Quality_SourceTimestamp = 2306;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_LastSeverity = 2307;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = 2308;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Comment = 2309;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Comment_SourceTimestamp = 2310;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ClientUserId = 2311;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AddComment_InputArguments = 2315;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AckedState = 2316;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AckedState_Id = 2317;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConfirmedState_Id = 2326;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Acknowledge_InputArguments = 2335;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Confirm_InputArguments = 2337;

        /// <remarks />
        public const uint Data_Static_UserScalar_BooleanValue = 2338;

        /// <remarks />
        public const uint Data_Static_UserScalar_SByteValue = 2339;

        /// <remarks />
        public const uint Data_Static_UserScalar_ByteValue = 2340;

        /// <remarks />
        public const uint Data_Static_UserScalar_Int16Value = 2341;

        /// <remarks />
        public const uint Data_Static_UserScalar_UInt16Value = 2342;

        /// <remarks />
        public const uint Data_Static_UserScalar_Int32Value = 2343;

        /// <remarks />
        public const uint Data_Static_UserScalar_UInt32Value = 2344;

        /// <remarks />
        public const uint Data_Static_UserScalar_Int64Value = 2345;

        /// <remarks />
        public const uint Data_Static_UserScalar_UInt64Value = 2346;

        /// <remarks />
        public const uint Data_Static_UserScalar_FloatValue = 2347;

        /// <remarks />
        public const uint Data_Static_UserScalar_DoubleValue = 2348;

        /// <remarks />
        public const uint Data_Static_UserScalar_StringValue = 2349;

        /// <remarks />
        public const uint Data_Static_UserScalar_DateTimeValue = 2350;

        /// <remarks />
        public const uint Data_Static_UserScalar_GuidValue = 2351;

        /// <remarks />
        public const uint Data_Static_UserScalar_ByteStringValue = 2352;

        /// <remarks />
        public const uint Data_Static_UserScalar_XmlElementValue = 2353;

        /// <remarks />
        public const uint Data_Static_UserScalar_NodeIdValue = 2354;

        /// <remarks />
        public const uint Data_Static_UserScalar_ExpandedNodeIdValue = 2355;

        /// <remarks />
        public const uint Data_Static_UserScalar_QualifiedNameValue = 2356;

        /// <remarks />
        public const uint Data_Static_UserScalar_LocalizedTextValue = 2357;

        /// <remarks />
        public const uint Data_Static_UserScalar_StatusCodeValue = 2358;

        /// <remarks />
        public const uint Data_Static_UserScalar_VariantValue = 2359;

        /// <remarks />
        public const uint Data_Static_UserArray_SimulationActive = 2361;

        /// <remarks />
        public const uint Data_Static_UserArray_GenerateValues_InputArguments = 2363;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EventId = 2365;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EventType = 2366;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_SourceNode = 2367;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_SourceName = 2368;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Time = 2369;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ReceiveTime = 2370;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Message = 2372;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Severity = 2373;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConditionClassId = 2374;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConditionClassName = 2375;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConditionName = 2378;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_BranchId = 2379;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Retain = 2380;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EnabledState = 2381;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EnabledState_Id = 2382;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Quality = 2390;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Quality_SourceTimestamp = 2391;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_LastSeverity = 2392;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_LastSeverity_SourceTimestamp = 2393;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Comment = 2394;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Comment_SourceTimestamp = 2395;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ClientUserId = 2396;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AddComment_InputArguments = 2400;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AckedState = 2401;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AckedState_Id = 2402;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConfirmedState_Id = 2411;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Acknowledge_InputArguments = 2420;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Confirm_InputArguments = 2422;

        /// <remarks />
        public const uint Data_Static_UserArray_BooleanValue = 2423;

        /// <remarks />
        public const uint Data_Static_UserArray_SByteValue = 2424;

        /// <remarks />
        public const uint Data_Static_UserArray_ByteValue = 2425;

        /// <remarks />
        public const uint Data_Static_UserArray_Int16Value = 2426;

        /// <remarks />
        public const uint Data_Static_UserArray_UInt16Value = 2427;

        /// <remarks />
        public const uint Data_Static_UserArray_Int32Value = 2428;

        /// <remarks />
        public const uint Data_Static_UserArray_UInt32Value = 2429;

        /// <remarks />
        public const uint Data_Static_UserArray_Int64Value = 2430;

        /// <remarks />
        public const uint Data_Static_UserArray_UInt64Value = 2431;

        /// <remarks />
        public const uint Data_Static_UserArray_FloatValue = 2432;

        /// <remarks />
        public const uint Data_Static_UserArray_DoubleValue = 2433;

        /// <remarks />
        public const uint Data_Static_UserArray_StringValue = 2434;

        /// <remarks />
        public const uint Data_Static_UserArray_DateTimeValue = 2435;

        /// <remarks />
        public const uint Data_Static_UserArray_GuidValue = 2436;

        /// <remarks />
        public const uint Data_Static_UserArray_ByteStringValue = 2437;

        /// <remarks />
        public const uint Data_Static_UserArray_XmlElementValue = 2438;

        /// <remarks />
        public const uint Data_Static_UserArray_NodeIdValue = 2439;

        /// <remarks />
        public const uint Data_Static_UserArray_ExpandedNodeIdValue = 2440;

        /// <remarks />
        public const uint Data_Static_UserArray_QualifiedNameValue = 2441;

        /// <remarks />
        public const uint Data_Static_UserArray_LocalizedTextValue = 2442;

        /// <remarks />
        public const uint Data_Static_UserArray_StatusCodeValue = 2443;

        /// <remarks />
        public const uint Data_Static_UserArray_VariantValue = 2444;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_SimulationActive = 2446;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_GenerateValues_InputArguments = 2448;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EventId = 2450;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EventType = 2451;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_SourceNode = 2452;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_SourceName = 2453;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Time = 2454;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ReceiveTime = 2455;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Message = 2457;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Severity = 2458;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionClassId = 2459;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionClassName = 2460;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionName = 2463;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_BranchId = 2464;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Retain = 2465;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EnabledState = 2466;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EnabledState_Id = 2467;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Quality = 2475;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Quality_SourceTimestamp = 2476;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_LastSeverity = 2477;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = 2478;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Comment = 2479;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Comment_SourceTimestamp = 2480;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ClientUserId = 2481;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AddComment_InputArguments = 2485;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AckedState = 2486;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AckedState_Id = 2487;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConfirmedState_Id = 2496;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Acknowledge_InputArguments = 2505;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Confirm_InputArguments = 2507;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_SByteValue = 2508;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_SByteValue_EURange = 2512;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_ByteValue = 2514;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_ByteValue_EURange = 2518;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int16Value = 2520;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int16Value_EURange = 2524;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt16Value = 2526;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt16Value_EURange = 2530;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int32Value = 2532;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int32Value_EURange = 2536;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt32Value = 2538;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt32Value_EURange = 2542;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int64Value = 2544;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int64Value_EURange = 2548;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt64Value = 2550;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt64Value_EURange = 2554;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_FloatValue = 2556;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_FloatValue_EURange = 2560;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_DoubleValue = 2562;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_DoubleValue_EURange = 2566;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_NumberValue = 2568;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_NumberValue_EURange = 2572;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_IntegerValue = 2574;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_IntegerValue_EURange = 2578;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UIntegerValue = 2580;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UIntegerValue_EURange = 2584;

        /// <remarks />
        public const uint Data_Static_AnalogArray_SimulationActive = 2587;

        /// <remarks />
        public const uint Data_Static_AnalogArray_GenerateValues_InputArguments = 2589;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EventId = 2591;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EventType = 2592;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_SourceNode = 2593;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_SourceName = 2594;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Time = 2595;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ReceiveTime = 2596;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Message = 2598;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Severity = 2599;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionClassId = 2600;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionClassName = 2601;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionName = 2604;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_BranchId = 2605;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Retain = 2606;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EnabledState = 2607;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EnabledState_Id = 2608;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Quality = 2616;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Quality_SourceTimestamp = 2617;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_LastSeverity = 2618;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = 2619;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Comment = 2620;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Comment_SourceTimestamp = 2621;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ClientUserId = 2622;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AddComment_InputArguments = 2626;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AckedState = 2627;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AckedState_Id = 2628;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConfirmedState_Id = 2637;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Acknowledge_InputArguments = 2646;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Confirm_InputArguments = 2648;

        /// <remarks />
        public const uint Data_Static_AnalogArray_SByteValue = 2649;

        /// <remarks />
        public const uint Data_Static_AnalogArray_SByteValue_EURange = 2653;

        /// <remarks />
        public const uint Data_Static_AnalogArray_ByteValue = 2655;

        /// <remarks />
        public const uint Data_Static_AnalogArray_ByteValue_EURange = 2659;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int16Value = 2661;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int16Value_EURange = 2665;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt16Value = 2667;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt16Value_EURange = 2671;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int32Value = 2673;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int32Value_EURange = 2677;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt32Value = 2679;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt32Value_EURange = 2683;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int64Value = 2685;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int64Value_EURange = 2689;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt64Value = 2691;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt64Value_EURange = 2695;

        /// <remarks />
        public const uint Data_Static_AnalogArray_FloatValue = 2697;

        /// <remarks />
        public const uint Data_Static_AnalogArray_FloatValue_EURange = 2701;

        /// <remarks />
        public const uint Data_Static_AnalogArray_DoubleValue = 2703;

        /// <remarks />
        public const uint Data_Static_AnalogArray_DoubleValue_EURange = 2707;

        /// <remarks />
        public const uint Data_Static_AnalogArray_NumberValue = 2709;

        /// <remarks />
        public const uint Data_Static_AnalogArray_NumberValue_EURange = 2713;

        /// <remarks />
        public const uint Data_Static_AnalogArray_IntegerValue = 2715;

        /// <remarks />
        public const uint Data_Static_AnalogArray_IntegerValue_EURange = 2719;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UIntegerValue = 2721;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UIntegerValue_EURange = 2725;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod1_InputArguments = 2729;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod1_OutputArguments = 2730;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod2_InputArguments = 2732;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod2_OutputArguments = 2733;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod3_InputArguments = 2735;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod3_OutputArguments = 2736;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod1_InputArguments = 2738;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod1_OutputArguments = 2739;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod2_InputArguments = 2741;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod2_OutputArguments = 2742;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod3_InputArguments = 2744;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod3_OutputArguments = 2745;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod1_InputArguments = 2747;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod1_OutputArguments = 2748;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod2_InputArguments = 2750;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod2_OutputArguments = 2751;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod1_InputArguments = 2753;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod1_OutputArguments = 2754;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod2_InputArguments = 2756;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod2_OutputArguments = 2757;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_SimulationActive = 2760;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_GenerateValues_InputArguments = 2762;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EventId = 2764;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EventType = 2765;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_SourceNode = 2766;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_SourceName = 2767;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Time = 2768;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ReceiveTime = 2769;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Message = 2771;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Severity = 2772;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionClassId = 2773;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionClassName = 2774;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionName = 2777;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_BranchId = 2778;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Retain = 2779;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EnabledState = 2780;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EnabledState_Id = 2781;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Quality = 2789;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Quality_SourceTimestamp = 2790;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_LastSeverity = 2791;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_LastSeverity_SourceTimestamp = 2792;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Comment = 2793;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Comment_SourceTimestamp = 2794;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ClientUserId = 2795;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AddComment_InputArguments = 2799;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AckedState = 2800;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AckedState_Id = 2801;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConfirmedState_Id = 2810;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Acknowledge_InputArguments = 2819;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Confirm_InputArguments = 2821;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_BooleanValue = 2822;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_SByteValue = 2823;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_ByteValue = 2824;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_Int16Value = 2825;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UInt16Value = 2826;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_Int32Value = 2827;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UInt32Value = 2828;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_Int64Value = 2829;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UInt64Value = 2830;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_FloatValue = 2831;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_DoubleValue = 2832;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_StringValue = 2833;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_DateTimeValue = 2834;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_GuidValue = 2835;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_ByteStringValue = 2836;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_XmlElementValue = 2837;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_NodeIdValue = 2838;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_ExpandedNodeIdValue = 2839;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_QualifiedNameValue = 2840;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_LocalizedTextValue = 2841;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_StatusCodeValue = 2842;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VariantValue = 2843;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_EnumerationValue = 2844;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_StructureValue = 2845;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_NumberValue = 2846;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_IntegerValue = 2847;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UIntegerValue = 2848;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VectorValue = 2849;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VectorValue_X = 3599;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VectorValue_Y = 3600;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VectorValue_Z = 3601;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar = 2850;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_SimulationActive = 2851;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_GenerateValues_InputArguments = 2853;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_EventId = 2855;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_EventType = 2856;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_SourceNode = 2857;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_SourceName = 2858;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Time = 2859;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ReceiveTime = 2860;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Message = 2862;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Severity = 2863;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ConditionClassId = 2864;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ConditionClassName = 2865;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ConditionName = 2868;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_BranchId = 2869;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Retain = 2870;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_EnabledState = 2871;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_EnabledState_Id = 2872;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Quality = 2880;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Quality_SourceTimestamp = 2881;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_LastSeverity = 2882;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_LastSeverity_SourceTimestamp = 2883;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Comment = 2884;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Comment_SourceTimestamp = 2885;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ClientUserId = 2886;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_AddComment_InputArguments = 2890;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_AckedState = 2891;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_AckedState_Id = 2892;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_ConfirmedState_Id = 2901;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Acknowledge_InputArguments = 2910;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_CycleComplete_Confirm_InputArguments = 2912;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_BooleanValue = 2913;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_SByteValue = 2914;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_ByteValue = 2915;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_Int16Value = 2916;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_UInt16Value = 2917;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_Int32Value = 2918;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_UInt32Value = 2919;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_Int64Value = 2920;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_UInt64Value = 2921;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_FloatValue = 2922;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_DoubleValue = 2923;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_StringValue = 2924;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_DateTimeValue = 2925;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_GuidValue = 2926;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_ByteStringValue = 2927;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_XmlElementValue = 2928;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_NodeIdValue = 2929;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_ExpandedNodeIdValue = 2930;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_QualifiedNameValue = 2931;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_LocalizedTextValue = 2932;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_StatusCodeValue = 2933;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_VariantValue = 2934;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_EnumerationValue = 2935;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_StructureValue = 2936;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_NumberValue = 2937;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_IntegerValue = 2938;

        /// <remarks />
        public const uint Data_Dynamic_StructureScalar_UIntegerValue = 2939;

        /// <remarks />
        public const uint Data_Dynamic_Array_SimulationActive = 2941;

        /// <remarks />
        public const uint Data_Dynamic_Array_GenerateValues_InputArguments = 2943;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EventId = 2945;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EventType = 2946;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_SourceNode = 2947;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_SourceName = 2948;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Time = 2949;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ReceiveTime = 2950;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Message = 2952;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Severity = 2953;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConditionClassId = 2954;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConditionClassName = 2955;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConditionName = 2958;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_BranchId = 2959;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Retain = 2960;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EnabledState = 2961;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EnabledState_Id = 2962;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Quality = 2970;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Quality_SourceTimestamp = 2971;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_LastSeverity = 2972;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_LastSeverity_SourceTimestamp = 2973;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Comment = 2974;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Comment_SourceTimestamp = 2975;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ClientUserId = 2976;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AddComment_InputArguments = 2980;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AckedState = 2981;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AckedState_Id = 2982;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConfirmedState_Id = 2991;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Acknowledge_InputArguments = 3000;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Confirm_InputArguments = 3002;

        /// <remarks />
        public const uint Data_Dynamic_Array_BooleanValue = 3003;

        /// <remarks />
        public const uint Data_Dynamic_Array_SByteValue = 3004;

        /// <remarks />
        public const uint Data_Dynamic_Array_ByteValue = 3005;

        /// <remarks />
        public const uint Data_Dynamic_Array_Int16Value = 3006;

        /// <remarks />
        public const uint Data_Dynamic_Array_UInt16Value = 3007;

        /// <remarks />
        public const uint Data_Dynamic_Array_Int32Value = 3008;

        /// <remarks />
        public const uint Data_Dynamic_Array_UInt32Value = 3009;

        /// <remarks />
        public const uint Data_Dynamic_Array_Int64Value = 3010;

        /// <remarks />
        public const uint Data_Dynamic_Array_UInt64Value = 3011;

        /// <remarks />
        public const uint Data_Dynamic_Array_FloatValue = 3012;

        /// <remarks />
        public const uint Data_Dynamic_Array_DoubleValue = 3013;

        /// <remarks />
        public const uint Data_Dynamic_Array_StringValue = 3014;

        /// <remarks />
        public const uint Data_Dynamic_Array_DateTimeValue = 3015;

        /// <remarks />
        public const uint Data_Dynamic_Array_GuidValue = 3016;

        /// <remarks />
        public const uint Data_Dynamic_Array_ByteStringValue = 3017;

        /// <remarks />
        public const uint Data_Dynamic_Array_XmlElementValue = 3018;

        /// <remarks />
        public const uint Data_Dynamic_Array_NodeIdValue = 3019;

        /// <remarks />
        public const uint Data_Dynamic_Array_ExpandedNodeIdValue = 3020;

        /// <remarks />
        public const uint Data_Dynamic_Array_QualifiedNameValue = 3021;

        /// <remarks />
        public const uint Data_Dynamic_Array_LocalizedTextValue = 3022;

        /// <remarks />
        public const uint Data_Dynamic_Array_StatusCodeValue = 3023;

        /// <remarks />
        public const uint Data_Dynamic_Array_VariantValue = 3024;

        /// <remarks />
        public const uint Data_Dynamic_Array_EnumerationValue = 3025;

        /// <remarks />
        public const uint Data_Dynamic_Array_StructureValue = 3026;

        /// <remarks />
        public const uint Data_Dynamic_Array_NumberValue = 3027;

        /// <remarks />
        public const uint Data_Dynamic_Array_IntegerValue = 3028;

        /// <remarks />
        public const uint Data_Dynamic_Array_UIntegerValue = 3029;

        /// <remarks />
        public const uint Data_Dynamic_Array_VectorValue = 3030;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_SimulationActive = 3032;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_GenerateValues_InputArguments = 3034;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EventId = 3036;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EventType = 3037;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_SourceNode = 3038;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_SourceName = 3039;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Time = 3040;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ReceiveTime = 3041;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Message = 3043;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Severity = 3044;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionClassId = 3045;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionClassName = 3046;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionName = 3049;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_BranchId = 3050;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Retain = 3051;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EnabledState = 3052;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EnabledState_Id = 3053;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Quality = 3061;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Quality_SourceTimestamp = 3062;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_LastSeverity = 3063;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = 3064;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Comment = 3065;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Comment_SourceTimestamp = 3066;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ClientUserId = 3067;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AddComment_InputArguments = 3071;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AckedState = 3072;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AckedState_Id = 3073;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConfirmedState_Id = 3082;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Acknowledge_InputArguments = 3091;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Confirm_InputArguments = 3093;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_BooleanValue = 3094;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_SByteValue = 3095;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_ByteValue = 3096;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_Int16Value = 3097;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_UInt16Value = 3098;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_Int32Value = 3099;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_UInt32Value = 3100;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_Int64Value = 3101;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_UInt64Value = 3102;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_FloatValue = 3103;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_DoubleValue = 3104;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_StringValue = 3105;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_DateTimeValue = 3106;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_GuidValue = 3107;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_ByteStringValue = 3108;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_XmlElementValue = 3109;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_NodeIdValue = 3110;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_ExpandedNodeIdValue = 3111;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_QualifiedNameValue = 3112;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_LocalizedTextValue = 3113;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_StatusCodeValue = 3114;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_VariantValue = 3115;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_SimulationActive = 3117;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_GenerateValues_InputArguments = 3119;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EventId = 3121;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EventType = 3122;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_SourceNode = 3123;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_SourceName = 3124;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Time = 3125;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ReceiveTime = 3126;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Message = 3128;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Severity = 3129;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionClassId = 3130;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionClassName = 3131;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionName = 3134;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_BranchId = 3135;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Retain = 3136;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EnabledState = 3137;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EnabledState_Id = 3138;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Quality = 3146;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Quality_SourceTimestamp = 3147;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_LastSeverity = 3148;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_LastSeverity_SourceTimestamp = 3149;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Comment = 3150;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Comment_SourceTimestamp = 3151;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ClientUserId = 3152;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AddComment_InputArguments = 3156;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AckedState = 3157;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AckedState_Id = 3158;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConfirmedState_Id = 3167;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Acknowledge_InputArguments = 3176;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Confirm_InputArguments = 3178;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_BooleanValue = 3179;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_SByteValue = 3180;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_ByteValue = 3181;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_Int16Value = 3182;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_UInt16Value = 3183;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_Int32Value = 3184;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_UInt32Value = 3185;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_Int64Value = 3186;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_UInt64Value = 3187;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_FloatValue = 3188;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_DoubleValue = 3189;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_StringValue = 3190;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_DateTimeValue = 3191;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_GuidValue = 3192;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_ByteStringValue = 3193;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_XmlElementValue = 3194;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_NodeIdValue = 3195;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_ExpandedNodeIdValue = 3196;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_QualifiedNameValue = 3197;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_LocalizedTextValue = 3198;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_StatusCodeValue = 3199;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_VariantValue = 3200;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_SimulationActive = 3202;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_GenerateValues_InputArguments = 3204;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EventId = 3206;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EventType = 3207;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_SourceNode = 3208;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_SourceName = 3209;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Time = 3210;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ReceiveTime = 3211;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Message = 3213;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Severity = 3214;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassId = 3215;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassName = 3216;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionName = 3219;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_BranchId = 3220;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Retain = 3221;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EnabledState = 3222;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EnabledState_Id = 3223;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Quality = 3231;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Quality_SourceTimestamp = 3232;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity = 3233;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = 3234;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Comment = 3235;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Comment_SourceTimestamp = 3236;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ClientUserId = 3237;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AddComment_InputArguments = 3241;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AckedState = 3242;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AckedState_Id = 3243;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConfirmedState_Id = 3252;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge_InputArguments = 3261;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Confirm_InputArguments = 3263;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_SByteValue = 3264;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_SByteValue_EURange = 3268;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_ByteValue = 3270;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_ByteValue_EURange = 3274;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int16Value = 3276;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int16Value_EURange = 3280;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt16Value = 3282;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt16Value_EURange = 3286;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int32Value = 3288;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int32Value_EURange = 3292;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt32Value = 3294;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt32Value_EURange = 3298;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int64Value = 3300;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int64Value_EURange = 3304;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt64Value = 3306;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt64Value_EURange = 3310;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_FloatValue = 3312;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_FloatValue_EURange = 3316;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_DoubleValue = 3318;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_DoubleValue_EURange = 3322;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_NumberValue = 3324;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_NumberValue_EURange = 3328;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_IntegerValue = 3330;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_IntegerValue_EURange = 3334;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UIntegerValue = 3336;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UIntegerValue_EURange = 3340;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_SimulationActive = 3343;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_GenerateValues_InputArguments = 3345;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EventId = 3347;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EventType = 3348;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_SourceNode = 3349;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_SourceName = 3350;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Time = 3351;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ReceiveTime = 3352;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Message = 3354;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Severity = 3355;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionClassId = 3356;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionClassName = 3357;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionName = 3360;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_BranchId = 3361;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Retain = 3362;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EnabledState = 3363;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EnabledState_Id = 3364;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Quality = 3372;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Quality_SourceTimestamp = 3373;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_LastSeverity = 3374;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = 3375;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Comment = 3376;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Comment_SourceTimestamp = 3377;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ClientUserId = 3378;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AddComment_InputArguments = 3382;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AckedState = 3383;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AckedState_Id = 3384;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConfirmedState_Id = 3393;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Acknowledge_InputArguments = 3402;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Confirm_InputArguments = 3404;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_SByteValue = 3405;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_SByteValue_EURange = 3409;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_ByteValue = 3411;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_ByteValue_EURange = 3415;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int16Value = 3417;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int16Value_EURange = 3421;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt16Value = 3423;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt16Value_EURange = 3427;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int32Value = 3429;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int32Value_EURange = 3433;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt32Value = 3435;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt32Value_EURange = 3439;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int64Value = 3441;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int64Value_EURange = 3445;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt64Value = 3447;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt64Value_EURange = 3451;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_FloatValue = 3453;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_FloatValue_EURange = 3457;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_DoubleValue = 3459;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_DoubleValue_EURange = 3463;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_NumberValue = 3465;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_NumberValue_EURange = 3469;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_IntegerValue = 3471;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_IntegerValue_EURange = 3475;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UIntegerValue = 3477;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UIntegerValue_EURange = 3481;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EventId = 3485;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EventType = 3486;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_SourceNode = 3487;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_SourceName = 3488;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Time = 3489;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ReceiveTime = 3490;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Message = 3492;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Severity = 3493;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ConditionClassId = 3494;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ConditionClassName = 3495;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ConditionName = 3498;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_BranchId = 3499;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Retain = 3500;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EnabledState = 3501;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EnabledState_Id = 3502;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Quality = 3510;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Quality_SourceTimestamp = 3511;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_LastSeverity = 3512;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_LastSeverity_SourceTimestamp = 3513;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Comment = 3514;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Comment_SourceTimestamp = 3515;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ClientUserId = 3516;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_AddComment_InputArguments = 3520;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_MonitoredNodeCount = 3521;

        /// <remarks />
        public const uint TestData_BinarySchema = 3529;

        /// <remarks />
        public const uint TestData_BinarySchema_NamespaceUri = 3531;

        /// <remarks />
        public const uint TestData_BinarySchema_Deprecated = 3532;

        /// <remarks />
        public const uint TestData_BinarySchema_ScalarValueDataType = 3533;

        /// <remarks />
        public const uint TestData_BinarySchema_ArrayValueDataType = 3536;

        /// <remarks />
        public const uint TestData_BinarySchema_UserScalarValueDataType = 3539;

        /// <remarks />
        public const uint TestData_BinarySchema_UserArrayValueDataType = 3542;

        /// <remarks />
        public const uint TestData_BinarySchema_Vector = 3545;

        /// <remarks />
        public const uint TestData_BinarySchema_WorkOrderStatusType = 3548;

        /// <remarks />
        public const uint TestData_BinarySchema_WorkOrderType = 3551;

        /// <remarks />
        public const uint TestData_XmlSchema = 3561;

        /// <remarks />
        public const uint TestData_XmlSchema_NamespaceUri = 3563;

        /// <remarks />
        public const uint TestData_XmlSchema_Deprecated = 3564;

        /// <remarks />
        public const uint TestData_XmlSchema_ScalarValueDataType = 3565;

        /// <remarks />
        public const uint TestData_XmlSchema_ArrayValueDataType = 3568;

        /// <remarks />
        public const uint TestData_XmlSchema_UserScalarValueDataType = 3571;

        /// <remarks />
        public const uint TestData_XmlSchema_UserArrayValueDataType = 3574;

        /// <remarks />
        public const uint TestData_XmlSchema_Vector = 3577;

        /// <remarks />
        public const uint TestData_XmlSchema_WorkOrderStatusType = 3580;

        /// <remarks />
        public const uint TestData_XmlSchema_WorkOrderType = 3583;
    }
    #endregion

    #region VariableType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableTypes
    {
        /// <remarks />
        public const uint TestDataVariableType = 1079;

        /// <remarks />
        public const uint ScalarValueVariableType = 1142;

        /// <remarks />
        public const uint VectorVariableType = 1916;
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
        public static readonly ExpandedNodeId ScalarValueObjectType_VectorValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_VectorValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_VectorValue_X = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_VectorValue_X, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_VectorValue_Y = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_VectorValue_Y, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_VectorValue_Z = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_VectorValue_Z, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId ArrayValueObjectType_VectorValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_VectorValue, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId VectorVariableType_X = new ExpandedNodeId(TestData.Variables.VectorVariableType_X, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId VectorVariableType_Y = new ExpandedNodeId(TestData.Variables.VectorVariableType_Y, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId VectorVariableType_Z = new ExpandedNodeId(TestData.Variables.VectorVariableType_Z, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Static_Scalar_VectorValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_VectorValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_VectorValue_X = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_VectorValue_X, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_VectorValue_Y = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_VectorValue_Y, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_VectorValue_Z = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_VectorValue_Z, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Static_Array_VectorValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_VectorValue, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_VectorValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_VectorValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_VectorValue_X = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_VectorValue_X, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_VectorValue_Y = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_VectorValue_Y, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_VectorValue_Z = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_VectorValue_Z, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Dynamic_Array_VectorValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_VectorValue, TestData.Namespaces.TestData);

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

        /// <remarks />
        public static readonly ExpandedNodeId VectorVariableType = new ExpandedNodeId(TestData.VariableTypes.VectorVariableType, TestData.Namespaces.TestData);
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
        public const string VectorValue = "VectorValue";

        /// <remarks />
        public const string VectorVariableType = "VectorVariableType";

        /// <remarks />
        public const string WorkOrderStatusType = "WorkOrderStatusType";

        /// <remarks />
        public const string WorkOrderType = "WorkOrderType";

        /// <remarks />
        public const string X = "X";

        /// <remarks />
        public const string XmlElementDataType = "XmlElementDataType";

        /// <remarks />
        public const string XmlElementValue = "XmlElementValue";

        /// <remarks />
        public const string Y = "Y";

        /// <remarks />
        public const string Z = "Z";
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