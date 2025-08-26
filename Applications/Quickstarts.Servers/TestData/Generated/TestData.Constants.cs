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
        public const uint ScalarStructureDataType = 1078;

        /// <remarks />
        public const uint ArrayValueDataType = 1446;

        /// <remarks />
        public const uint BooleanDataType = 1688;

        /// <remarks />
        public const uint SByteDataType = 1689;

        /// <remarks />
        public const uint ByteDataType = 1690;

        /// <remarks />
        public const uint Int16DataType = 1691;

        /// <remarks />
        public const uint UInt16DataType = 1692;

        /// <remarks />
        public const uint Int32DataType = 1693;

        /// <remarks />
        public const uint UInt32DataType = 1694;

        /// <remarks />
        public const uint Int64DataType = 1695;

        /// <remarks />
        public const uint UInt64DataType = 1696;

        /// <remarks />
        public const uint FloatDataType = 1697;

        /// <remarks />
        public const uint DoubleDataType = 1698;

        /// <remarks />
        public const uint StringDataType = 1699;

        /// <remarks />
        public const uint DateTimeDataType = 1700;

        /// <remarks />
        public const uint GuidDataType = 1701;

        /// <remarks />
        public const uint ByteStringDataType = 1702;

        /// <remarks />
        public const uint XmlElementDataType = 1703;

        /// <remarks />
        public const uint NodeIdDataType = 1704;

        /// <remarks />
        public const uint ExpandedNodeIdDataType = 1705;

        /// <remarks />
        public const uint QualifiedNameDataType = 1706;

        /// <remarks />
        public const uint LocalizedTextDataType = 1707;

        /// <remarks />
        public const uint StatusCodeDataType = 1708;

        /// <remarks />
        public const uint VariantDataType = 1709;

        /// <remarks />
        public const uint UserScalarValueDataType = 1710;

        /// <remarks />
        public const uint UserArrayValueDataType = 1802;

        /// <remarks />
        public const uint Vector = 1888;

        /// <remarks />
        public const uint VectorUnion = 3584;

        /// <remarks />
        public const uint VectorWithOptionalFields = 3585;

        /// <remarks />
        public const uint MultipleVectors = 3615;

        /// <remarks />
        public const uint WorkOrderStatusType = 1893;

        /// <remarks />
        public const uint WorkOrderType = 1894;
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
        public const uint ScalarValueObjectType_CycleComplete_Disable = 1153;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Enable = 1154;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AddComment = 1155;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Acknowledge = 1175;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Disable = 1247;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Enable = 1248;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_AddComment = 1249;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Acknowledge = 1269;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Disable = 1342;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Enable = 1343;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AddComment = 1344;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Acknowledge = 1364;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Disable = 1493;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Enable = 1494;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AddComment = 1495;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Acknowledge = 1515;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Disable = 1584;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Enable = 1585;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AddComment = 1586;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Acknowledge = 1606;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Disable = 1748;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Enable = 1749;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AddComment = 1750;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Acknowledge = 1770;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Disable = 1840;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Enable = 1841;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AddComment = 1842;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Acknowledge = 1862;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod1 = 1902;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod2 = 1905;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod3 = 1908;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod1 = 1911;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod2 = 1914;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod3 = 1917;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod1 = 1920;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod2 = 1923;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod1 = 1926;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod2 = 1929;

        /// <remarks />
        public const uint Data_Static_Scalar_GenerateValues = 1978;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Disable = 2013;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Enable = 2014;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AddComment = 2015;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Acknowledge = 2035;

        /// <remarks />
        public const uint Data_Static_Structure_GenerateValues = 2072;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Disable = 2107;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Enable = 2108;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_AddComment = 2109;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Acknowledge = 2129;

        /// <remarks />
        public const uint Data_Static_Array_GenerateValues = 2167;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Disable = 2202;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Enable = 2203;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AddComment = 2204;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Acknowledge = 2224;

        /// <remarks />
        public const uint Data_Static_UserScalar_GenerateValues = 2258;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Disable = 2293;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Enable = 2294;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AddComment = 2295;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Acknowledge = 2315;

        /// <remarks />
        public const uint Data_Static_UserArray_GenerateValues = 2343;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Disable = 2378;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Enable = 2379;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AddComment = 2380;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Acknowledge = 2400;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_GenerateValues = 2428;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Disable = 2463;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Enable = 2464;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AddComment = 2465;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Acknowledge = 2485;

        /// <remarks />
        public const uint Data_Static_AnalogArray_GenerateValues = 2569;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Disable = 2604;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Enable = 2605;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AddComment = 2606;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Acknowledge = 2626;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod1 = 2709;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod2 = 2712;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod3 = 2715;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod1 = 2718;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod2 = 2721;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod3 = 2724;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod1 = 2727;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod2 = 2730;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod1 = 2733;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod2 = 2736;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_GenerateValues = 2742;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Disable = 2777;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Enable = 2778;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AddComment = 2779;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Acknowledge = 2799;

        /// <remarks />
        public const uint Data_Dynamic_Structure_GenerateValues = 2836;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Disable = 2871;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Enable = 2872;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_AddComment = 2873;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Acknowledge = 2893;

        /// <remarks />
        public const uint Data_Dynamic_Array_GenerateValues = 2931;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Disable = 2966;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Enable = 2967;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AddComment = 2968;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Acknowledge = 2988;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_GenerateValues = 3022;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Disable = 3057;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Enable = 3058;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AddComment = 3059;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Acknowledge = 3079;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_GenerateValues = 3107;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Disable = 3142;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Enable = 3143;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AddComment = 3144;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Acknowledge = 3164;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_GenerateValues = 3192;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Disable = 3227;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Enable = 3228;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AddComment = 3229;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge = 3249;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_GenerateValues = 3333;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Disable = 3368;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Enable = 3369;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AddComment = 3370;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Acknowledge = 3390;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Disable = 3506;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Enable = 3507;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_AddComment = 3508;
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
        public const uint Data = 1974;

        /// <remarks />
        public const uint Data_Static = 1975;

        /// <remarks />
        public const uint Data_Static_Scalar = 1976;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete = 1980;

        /// <remarks />
        public const uint Data_Static_Structure = 2070;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete = 2074;

        /// <remarks />
        public const uint Data_Static_Array = 2165;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete = 2169;

        /// <remarks />
        public const uint Data_Static_UserScalar = 2256;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete = 2260;

        /// <remarks />
        public const uint Data_Static_UserArray = 2341;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete = 2345;

        /// <remarks />
        public const uint Data_Static_AnalogScalar = 2426;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete = 2430;

        /// <remarks />
        public const uint Data_Static_AnalogArray = 2567;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete = 2571;

        /// <remarks />
        public const uint Data_Static_MethodTest = 2708;

        /// <remarks />
        public const uint Data_Dynamic = 2739;

        /// <remarks />
        public const uint Data_Dynamic_Scalar = 2740;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete = 2744;

        /// <remarks />
        public const uint Data_Dynamic_Structure = 2834;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete = 2838;

        /// <remarks />
        public const uint Data_Dynamic_Array = 2929;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete = 2933;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar = 3020;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete = 3024;

        /// <remarks />
        public const uint Data_Dynamic_UserArray = 3105;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete = 3109;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar = 3190;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete = 3194;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray = 3331;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete = 3335;

        /// <remarks />
        public const uint Data_Conditions = 3472;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus = 3473;

        /// <remarks />
        public const uint ScalarStructureDataType_Encoding_DefaultBinary = 3511;

        /// <remarks />
        public const uint ArrayValueDataType_Encoding_DefaultBinary = 3512;

        /// <remarks />
        public const uint UserScalarValueDataType_Encoding_DefaultBinary = 3513;

        /// <remarks />
        public const uint UserArrayValueDataType_Encoding_DefaultBinary = 3514;

        /// <remarks />
        public const uint Vector_Encoding_DefaultBinary = 3515;

        /// <remarks />
        public const uint VectorUnion_Encoding_DefaultBinary = 3590;

        /// <remarks />
        public const uint VectorWithOptionalFields_Encoding_DefaultBinary = 3591;

        /// <remarks />
        public const uint MultipleVectors_Encoding_DefaultBinary = 3618;

        /// <remarks />
        public const uint WorkOrderStatusType_Encoding_DefaultBinary = 3516;

        /// <remarks />
        public const uint WorkOrderType_Encoding_DefaultBinary = 3517;

        /// <remarks />
        public const uint ScalarStructureDataType_Encoding_DefaultXml = 3543;

        /// <remarks />
        public const uint ArrayValueDataType_Encoding_DefaultXml = 3544;

        /// <remarks />
        public const uint UserScalarValueDataType_Encoding_DefaultXml = 3545;

        /// <remarks />
        public const uint UserArrayValueDataType_Encoding_DefaultXml = 3546;

        /// <remarks />
        public const uint Vector_Encoding_DefaultXml = 3547;

        /// <remarks />
        public const uint VectorUnion_Encoding_DefaultXml = 3598;

        /// <remarks />
        public const uint VectorWithOptionalFields_Encoding_DefaultXml = 3599;

        /// <remarks />
        public const uint MultipleVectors_Encoding_DefaultXml = 3622;

        /// <remarks />
        public const uint WorkOrderStatusType_Encoding_DefaultXml = 3548;

        /// <remarks />
        public const uint WorkOrderType_Encoding_DefaultXml = 3549;

        /// <remarks />
        public const uint ScalarStructureDataType_Encoding_DefaultJson = 3575;

        /// <remarks />
        public const uint ArrayValueDataType_Encoding_DefaultJson = 3576;

        /// <remarks />
        public const uint UserScalarValueDataType_Encoding_DefaultJson = 3577;

        /// <remarks />
        public const uint UserArrayValueDataType_Encoding_DefaultJson = 3578;

        /// <remarks />
        public const uint Vector_Encoding_DefaultJson = 3579;

        /// <remarks />
        public const uint VectorUnion_Encoding_DefaultJson = 3606;

        /// <remarks />
        public const uint VectorWithOptionalFields_Encoding_DefaultJson = 3607;

        /// <remarks />
        public const uint MultipleVectors_Encoding_DefaultJson = 3626;

        /// <remarks />
        public const uint WorkOrderStatusType_Encoding_DefaultJson = 3580;

        /// <remarks />
        public const uint WorkOrderType_Encoding_DefaultJson = 3581;
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
        public const uint ScalarValueObjectType = 1116;

        /// <remarks />
        public const uint StructureValueObjectType = 1210;

        /// <remarks />
        public const uint AnalogScalarValueObjectType = 1305;

        /// <remarks />
        public const uint ArrayValueObjectType = 1456;

        /// <remarks />
        public const uint AnalogArrayValueObjectType = 1547;

        /// <remarks />
        public const uint UserScalarValueObjectType = 1711;

        /// <remarks />
        public const uint UserArrayValueObjectType = 1803;

        /// <remarks />
        public const uint MethodTestType = 1901;

        /// <remarks />
        public const uint TestSystemConditionType = 1932;
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
        public const uint ScalarStructureVariableType_BooleanValue = 1080;

        /// <remarks />
        public const uint ScalarStructureVariableType_SByteValue = 1081;

        /// <remarks />
        public const uint ScalarStructureVariableType_ByteValue = 1082;

        /// <remarks />
        public const uint ScalarStructureVariableType_Int16Value = 1083;

        /// <remarks />
        public const uint ScalarStructureVariableType_UInt16Value = 1084;

        /// <remarks />
        public const uint ScalarStructureVariableType_Int32Value = 1085;

        /// <remarks />
        public const uint ScalarStructureVariableType_UInt32Value = 1086;

        /// <remarks />
        public const uint ScalarStructureVariableType_Int64Value = 1087;

        /// <remarks />
        public const uint ScalarStructureVariableType_UInt64Value = 1088;

        /// <remarks />
        public const uint ScalarStructureVariableType_FloatValue = 1089;

        /// <remarks />
        public const uint ScalarStructureVariableType_DoubleValue = 1090;

        /// <remarks />
        public const uint ScalarStructureVariableType_StringValue = 1091;

        /// <remarks />
        public const uint ScalarStructureVariableType_DateTimeValue = 1092;

        /// <remarks />
        public const uint ScalarStructureVariableType_GuidValue = 1093;

        /// <remarks />
        public const uint ScalarStructureVariableType_ByteStringValue = 1094;

        /// <remarks />
        public const uint ScalarStructureVariableType_XmlElementValue = 1095;

        /// <remarks />
        public const uint ScalarStructureVariableType_NodeIdValue = 1096;

        /// <remarks />
        public const uint ScalarStructureVariableType_ExpandedNodeIdValue = 1097;

        /// <remarks />
        public const uint ScalarStructureVariableType_QualifiedNameValue = 1098;

        /// <remarks />
        public const uint ScalarStructureVariableType_LocalizedTextValue = 1099;

        /// <remarks />
        public const uint ScalarStructureVariableType_StatusCodeValue = 1100;

        /// <remarks />
        public const uint ScalarStructureVariableType_VariantValue = 1101;

        /// <remarks />
        public const uint ScalarStructureVariableType_EnumerationValue = 1102;

        /// <remarks />
        public const uint ScalarStructureVariableType_StructureValue = 1103;

        /// <remarks />
        public const uint ScalarStructureVariableType_NumberValue = 1104;

        /// <remarks />
        public const uint ScalarStructureVariableType_IntegerValue = 1105;

        /// <remarks />
        public const uint ScalarStructureVariableType_UIntegerValue = 1106;

        /// <remarks />
        public const uint ScalarValueObjectType_GenerateValues_InputArguments = 1119;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EventId = 1121;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EventType = 1122;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_SourceNode = 1123;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_SourceName = 1124;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Time = 1125;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ReceiveTime = 1126;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Message = 1128;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Severity = 1129;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConditionClassId = 1130;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConditionClassName = 1131;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConditionName = 1134;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_BranchId = 1135;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Retain = 1136;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EnabledState = 1137;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_EnabledState_Id = 1138;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Quality = 1146;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = 1147;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_LastSeverity = 1148;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1149;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Comment = 1150;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = 1151;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ClientUserId = 1152;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AddComment_InputArguments = 1156;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AckedState = 1157;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_AckedState_Id = 1158;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_ConfirmedState_Id = 1167;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = 1176;

        /// <remarks />
        public const uint ScalarValueObjectType_CycleComplete_Confirm_InputArguments = 1178;

        /// <remarks />
        public const uint ScalarValueObjectType_BooleanValue = 1179;

        /// <remarks />
        public const uint ScalarValueObjectType_SByteValue = 1180;

        /// <remarks />
        public const uint ScalarValueObjectType_ByteValue = 1181;

        /// <remarks />
        public const uint ScalarValueObjectType_Int16Value = 1182;

        /// <remarks />
        public const uint ScalarValueObjectType_UInt16Value = 1183;

        /// <remarks />
        public const uint ScalarValueObjectType_Int32Value = 1184;

        /// <remarks />
        public const uint ScalarValueObjectType_UInt32Value = 1185;

        /// <remarks />
        public const uint ScalarValueObjectType_Int64Value = 1186;

        /// <remarks />
        public const uint ScalarValueObjectType_UInt64Value = 1187;

        /// <remarks />
        public const uint ScalarValueObjectType_FloatValue = 1188;

        /// <remarks />
        public const uint ScalarValueObjectType_DoubleValue = 1189;

        /// <remarks />
        public const uint ScalarValueObjectType_StringValue = 1190;

        /// <remarks />
        public const uint ScalarValueObjectType_DateTimeValue = 1191;

        /// <remarks />
        public const uint ScalarValueObjectType_GuidValue = 1192;

        /// <remarks />
        public const uint ScalarValueObjectType_ByteStringValue = 1193;

        /// <remarks />
        public const uint ScalarValueObjectType_XmlElementValue = 1194;

        /// <remarks />
        public const uint ScalarValueObjectType_NodeIdValue = 1195;

        /// <remarks />
        public const uint ScalarValueObjectType_ExpandedNodeIdValue = 1196;

        /// <remarks />
        public const uint ScalarValueObjectType_QualifiedNameValue = 1197;

        /// <remarks />
        public const uint ScalarValueObjectType_LocalizedTextValue = 1198;

        /// <remarks />
        public const uint ScalarValueObjectType_StatusCodeValue = 1199;

        /// <remarks />
        public const uint ScalarValueObjectType_VariantValue = 1200;

        /// <remarks />
        public const uint ScalarValueObjectType_EnumerationValue = 1201;

        /// <remarks />
        public const uint ScalarValueObjectType_StructureValue = 1202;

        /// <remarks />
        public const uint ScalarValueObjectType_NumberValue = 1203;

        /// <remarks />
        public const uint ScalarValueObjectType_IntegerValue = 1204;

        /// <remarks />
        public const uint ScalarValueObjectType_UIntegerValue = 1205;

        /// <remarks />
        public const uint ScalarValueObjectType_VectorValue = 1206;

        /// <remarks />
        public const uint ScalarValueObjectType_VectorValue_X = 1207;

        /// <remarks />
        public const uint ScalarValueObjectType_VectorValue_Y = 1208;

        /// <remarks />
        public const uint ScalarValueObjectType_VectorValue_Z = 1209;

        /// <remarks />
        public const uint ScalarValueObjectType_VectorUnionValue = 3582;

        /// <remarks />
        public const uint ScalarValueObjectType_VectorWithOptionalFieldsValue = 3583;

        /// <remarks />
        public const uint ScalarValueObjectType_MultipleVectorsValue = 3614;

        /// <remarks />
        public const uint StructureValueObjectType_GenerateValues_InputArguments = 1213;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_EventId = 1215;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_EventType = 1216;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_SourceNode = 1217;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_SourceName = 1218;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Time = 1219;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_ReceiveTime = 1220;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Message = 1222;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Severity = 1223;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_ConditionClassId = 1224;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_ConditionClassName = 1225;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_ConditionName = 1228;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_BranchId = 1229;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Retain = 1230;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_EnabledState = 1231;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_EnabledState_Id = 1232;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Quality = 1240;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Quality_SourceTimestamp = 1241;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_LastSeverity = 1242;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1243;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Comment = 1244;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Comment_SourceTimestamp = 1245;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_ClientUserId = 1246;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_AddComment_InputArguments = 1250;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_AckedState = 1251;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_AckedState_Id = 1252;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_ConfirmedState_Id = 1261;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Acknowledge_InputArguments = 1270;

        /// <remarks />
        public const uint StructureValueObjectType_CycleComplete_Confirm_InputArguments = 1272;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure = 1273;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_BooleanValue = 1274;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_SByteValue = 1275;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_ByteValue = 1276;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_Int16Value = 1277;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_UInt16Value = 1278;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_Int32Value = 1279;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_UInt32Value = 1280;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_Int64Value = 1281;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_UInt64Value = 1282;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_FloatValue = 1283;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_DoubleValue = 1284;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_StringValue = 1285;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_DateTimeValue = 1286;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_GuidValue = 1287;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_ByteStringValue = 1288;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_XmlElementValue = 1289;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_NodeIdValue = 1290;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_ExpandedNodeIdValue = 1291;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_QualifiedNameValue = 1292;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_LocalizedTextValue = 1293;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_StatusCodeValue = 1294;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_VariantValue = 1295;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_EnumerationValue = 1296;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_StructureValue = 1297;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_NumberValue = 1298;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_IntegerValue = 1299;

        /// <remarks />
        public const uint StructureValueObjectType_ScalarStructure_UIntegerValue = 1300;

        /// <remarks />
        public const uint StructureValueObjectType_VectorStructure = 1301;

        /// <remarks />
        public const uint StructureValueObjectType_VectorStructure_X = 1302;

        /// <remarks />
        public const uint StructureValueObjectType_VectorStructure_Y = 1303;

        /// <remarks />
        public const uint StructureValueObjectType_VectorStructure_Z = 1304;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_GenerateValues_InputArguments = 1308;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EventId = 1310;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EventType = 1311;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_SourceNode = 1312;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_SourceName = 1313;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Time = 1314;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ReceiveTime = 1315;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Message = 1317;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Severity = 1318;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConditionClassId = 1319;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConditionClassName = 1320;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConditionName = 1323;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_BranchId = 1324;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Retain = 1325;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EnabledState = 1326;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_EnabledState_Id = 1327;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Quality = 1335;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = 1336;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_LastSeverity = 1337;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1338;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Comment = 1339;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = 1340;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ClientUserId = 1341;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AddComment_InputArguments = 1345;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AckedState = 1346;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_AckedState_Id = 1347;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_ConfirmedState_Id = 1356;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = 1365;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_CycleComplete_Confirm_InputArguments = 1367;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_SByteValue = 1368;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_SByteValue_EURange = 1372;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_ByteValue = 1374;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_ByteValue_EURange = 1378;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int16Value = 1380;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int16Value_EURange = 1384;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt16Value = 1386;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt16Value_EURange = 1390;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int32Value = 1392;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int32Value_EURange = 1396;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt32Value = 1398;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt32Value_EURange = 1402;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int64Value = 1404;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_Int64Value_EURange = 1408;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt64Value = 1410;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UInt64Value_EURange = 1414;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_FloatValue = 1416;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_FloatValue_EURange = 1420;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_DoubleValue = 1422;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_DoubleValue_EURange = 1426;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_NumberValue = 1428;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_NumberValue_EURange = 1432;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_IntegerValue = 1434;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_IntegerValue_EURange = 1438;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UIntegerValue = 1440;

        /// <remarks />
        public const uint AnalogScalarValueObjectType_UIntegerValue_EURange = 1444;

        /// <remarks />
        public const uint ArrayValueObjectType_GenerateValues_InputArguments = 1459;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EventId = 1461;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EventType = 1462;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_SourceNode = 1463;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_SourceName = 1464;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Time = 1465;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ReceiveTime = 1466;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Message = 1468;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Severity = 1469;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConditionClassId = 1470;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConditionClassName = 1471;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConditionName = 1474;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_BranchId = 1475;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Retain = 1476;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EnabledState = 1477;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_EnabledState_Id = 1478;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Quality = 1486;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = 1487;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_LastSeverity = 1488;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1489;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Comment = 1490;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = 1491;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ClientUserId = 1492;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AddComment_InputArguments = 1496;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AckedState = 1497;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_AckedState_Id = 1498;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_ConfirmedState_Id = 1507;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = 1516;

        /// <remarks />
        public const uint ArrayValueObjectType_CycleComplete_Confirm_InputArguments = 1518;

        /// <remarks />
        public const uint ArrayValueObjectType_BooleanValue = 1519;

        /// <remarks />
        public const uint ArrayValueObjectType_SByteValue = 1520;

        /// <remarks />
        public const uint ArrayValueObjectType_ByteValue = 1521;

        /// <remarks />
        public const uint ArrayValueObjectType_Int16Value = 1522;

        /// <remarks />
        public const uint ArrayValueObjectType_UInt16Value = 1523;

        /// <remarks />
        public const uint ArrayValueObjectType_Int32Value = 1524;

        /// <remarks />
        public const uint ArrayValueObjectType_UInt32Value = 1525;

        /// <remarks />
        public const uint ArrayValueObjectType_Int64Value = 1526;

        /// <remarks />
        public const uint ArrayValueObjectType_UInt64Value = 1527;

        /// <remarks />
        public const uint ArrayValueObjectType_FloatValue = 1528;

        /// <remarks />
        public const uint ArrayValueObjectType_DoubleValue = 1529;

        /// <remarks />
        public const uint ArrayValueObjectType_StringValue = 1530;

        /// <remarks />
        public const uint ArrayValueObjectType_DateTimeValue = 1531;

        /// <remarks />
        public const uint ArrayValueObjectType_GuidValue = 1532;

        /// <remarks />
        public const uint ArrayValueObjectType_ByteStringValue = 1533;

        /// <remarks />
        public const uint ArrayValueObjectType_XmlElementValue = 1534;

        /// <remarks />
        public const uint ArrayValueObjectType_NodeIdValue = 1535;

        /// <remarks />
        public const uint ArrayValueObjectType_ExpandedNodeIdValue = 1536;

        /// <remarks />
        public const uint ArrayValueObjectType_QualifiedNameValue = 1537;

        /// <remarks />
        public const uint ArrayValueObjectType_LocalizedTextValue = 1538;

        /// <remarks />
        public const uint ArrayValueObjectType_StatusCodeValue = 1539;

        /// <remarks />
        public const uint ArrayValueObjectType_VariantValue = 1540;

        /// <remarks />
        public const uint ArrayValueObjectType_EnumerationValue = 1541;

        /// <remarks />
        public const uint ArrayValueObjectType_StructureValue = 1542;

        /// <remarks />
        public const uint ArrayValueObjectType_NumberValue = 1543;

        /// <remarks />
        public const uint ArrayValueObjectType_IntegerValue = 1544;

        /// <remarks />
        public const uint ArrayValueObjectType_UIntegerValue = 1545;

        /// <remarks />
        public const uint ArrayValueObjectType_VectorValue = 1546;

        /// <remarks />
        public const uint ArrayValueObjectType_VectorUnionValue = 3608;

        /// <remarks />
        public const uint ArrayValueObjectType_VectorWithOptionalFieldsValue = 3609;

        /// <remarks />
        public const uint ArrayValueObjectType_MultipleVectorsValue = 3627;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_GenerateValues_InputArguments = 1550;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EventId = 1552;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EventType = 1553;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_SourceNode = 1554;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_SourceName = 1555;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Time = 1556;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ReceiveTime = 1557;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Message = 1559;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Severity = 1560;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConditionClassId = 1561;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConditionClassName = 1562;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConditionName = 1565;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_BranchId = 1566;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Retain = 1567;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EnabledState = 1568;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_EnabledState_Id = 1569;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Quality = 1577;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = 1578;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_LastSeverity = 1579;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1580;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Comment = 1581;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = 1582;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ClientUserId = 1583;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AddComment_InputArguments = 1587;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AckedState = 1588;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_AckedState_Id = 1589;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_ConfirmedState_Id = 1598;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = 1607;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_CycleComplete_Confirm_InputArguments = 1609;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_SByteValue = 1610;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_SByteValue_EURange = 1614;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_ByteValue = 1616;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_ByteValue_EURange = 1620;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int16Value = 1622;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int16Value_EURange = 1626;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt16Value = 1628;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt16Value_EURange = 1632;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int32Value = 1634;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int32Value_EURange = 1638;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt32Value = 1640;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt32Value_EURange = 1644;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int64Value = 1646;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_Int64Value_EURange = 1650;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt64Value = 1652;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UInt64Value_EURange = 1656;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_FloatValue = 1658;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_FloatValue_EURange = 1662;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_DoubleValue = 1664;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_DoubleValue_EURange = 1668;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_NumberValue = 1670;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_NumberValue_EURange = 1674;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_IntegerValue = 1676;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_IntegerValue_EURange = 1680;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UIntegerValue = 1682;

        /// <remarks />
        public const uint AnalogArrayValueObjectType_UIntegerValue_EURange = 1686;

        /// <remarks />
        public const uint UserScalarValueObjectType_GenerateValues_InputArguments = 1714;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EventId = 1716;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EventType = 1717;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_SourceNode = 1718;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_SourceName = 1719;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Time = 1720;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ReceiveTime = 1721;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Message = 1723;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Severity = 1724;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConditionClassId = 1725;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConditionClassName = 1726;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConditionName = 1729;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_BranchId = 1730;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Retain = 1731;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EnabledState = 1732;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_EnabledState_Id = 1733;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Quality = 1741;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Quality_SourceTimestamp = 1742;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_LastSeverity = 1743;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1744;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Comment = 1745;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Comment_SourceTimestamp = 1746;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ClientUserId = 1747;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AddComment_InputArguments = 1751;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AckedState = 1752;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_AckedState_Id = 1753;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_ConfirmedState_Id = 1762;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Acknowledge_InputArguments = 1771;

        /// <remarks />
        public const uint UserScalarValueObjectType_CycleComplete_Confirm_InputArguments = 1773;

        /// <remarks />
        public const uint UserScalarValueObjectType_BooleanValue = 1774;

        /// <remarks />
        public const uint UserScalarValueObjectType_SByteValue = 1775;

        /// <remarks />
        public const uint UserScalarValueObjectType_ByteValue = 1776;

        /// <remarks />
        public const uint UserScalarValueObjectType_Int16Value = 1777;

        /// <remarks />
        public const uint UserScalarValueObjectType_UInt16Value = 1778;

        /// <remarks />
        public const uint UserScalarValueObjectType_Int32Value = 1779;

        /// <remarks />
        public const uint UserScalarValueObjectType_UInt32Value = 1780;

        /// <remarks />
        public const uint UserScalarValueObjectType_Int64Value = 1781;

        /// <remarks />
        public const uint UserScalarValueObjectType_UInt64Value = 1782;

        /// <remarks />
        public const uint UserScalarValueObjectType_FloatValue = 1783;

        /// <remarks />
        public const uint UserScalarValueObjectType_DoubleValue = 1784;

        /// <remarks />
        public const uint UserScalarValueObjectType_StringValue = 1785;

        /// <remarks />
        public const uint UserScalarValueObjectType_DateTimeValue = 1786;

        /// <remarks />
        public const uint UserScalarValueObjectType_GuidValue = 1787;

        /// <remarks />
        public const uint UserScalarValueObjectType_ByteStringValue = 1788;

        /// <remarks />
        public const uint UserScalarValueObjectType_XmlElementValue = 1789;

        /// <remarks />
        public const uint UserScalarValueObjectType_NodeIdValue = 1790;

        /// <remarks />
        public const uint UserScalarValueObjectType_ExpandedNodeIdValue = 1791;

        /// <remarks />
        public const uint UserScalarValueObjectType_QualifiedNameValue = 1792;

        /// <remarks />
        public const uint UserScalarValueObjectType_LocalizedTextValue = 1793;

        /// <remarks />
        public const uint UserScalarValueObjectType_StatusCodeValue = 1794;

        /// <remarks />
        public const uint UserScalarValueObjectType_VariantValue = 1795;

        /// <remarks />
        public const uint UserArrayValueObjectType_GenerateValues_InputArguments = 1806;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EventId = 1808;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EventType = 1809;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_SourceNode = 1810;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_SourceName = 1811;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Time = 1812;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ReceiveTime = 1813;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Message = 1815;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Severity = 1816;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConditionClassId = 1817;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConditionClassName = 1818;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConditionName = 1821;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_BranchId = 1822;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Retain = 1823;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EnabledState = 1824;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_EnabledState_Id = 1825;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Quality = 1833;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Quality_SourceTimestamp = 1834;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_LastSeverity = 1835;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = 1836;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Comment = 1837;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Comment_SourceTimestamp = 1838;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ClientUserId = 1839;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AddComment_InputArguments = 1843;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AckedState = 1844;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_AckedState_Id = 1845;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_ConfirmedState_Id = 1854;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Acknowledge_InputArguments = 1863;

        /// <remarks />
        public const uint UserArrayValueObjectType_CycleComplete_Confirm_InputArguments = 1865;

        /// <remarks />
        public const uint UserArrayValueObjectType_BooleanValue = 1866;

        /// <remarks />
        public const uint UserArrayValueObjectType_SByteValue = 1867;

        /// <remarks />
        public const uint UserArrayValueObjectType_ByteValue = 1868;

        /// <remarks />
        public const uint UserArrayValueObjectType_Int16Value = 1869;

        /// <remarks />
        public const uint UserArrayValueObjectType_UInt16Value = 1870;

        /// <remarks />
        public const uint UserArrayValueObjectType_Int32Value = 1871;

        /// <remarks />
        public const uint UserArrayValueObjectType_UInt32Value = 1872;

        /// <remarks />
        public const uint UserArrayValueObjectType_Int64Value = 1873;

        /// <remarks />
        public const uint UserArrayValueObjectType_UInt64Value = 1874;

        /// <remarks />
        public const uint UserArrayValueObjectType_FloatValue = 1875;

        /// <remarks />
        public const uint UserArrayValueObjectType_DoubleValue = 1876;

        /// <remarks />
        public const uint UserArrayValueObjectType_StringValue = 1877;

        /// <remarks />
        public const uint UserArrayValueObjectType_DateTimeValue = 1878;

        /// <remarks />
        public const uint UserArrayValueObjectType_GuidValue = 1879;

        /// <remarks />
        public const uint UserArrayValueObjectType_ByteStringValue = 1880;

        /// <remarks />
        public const uint UserArrayValueObjectType_XmlElementValue = 1881;

        /// <remarks />
        public const uint UserArrayValueObjectType_NodeIdValue = 1882;

        /// <remarks />
        public const uint UserArrayValueObjectType_ExpandedNodeIdValue = 1883;

        /// <remarks />
        public const uint UserArrayValueObjectType_QualifiedNameValue = 1884;

        /// <remarks />
        public const uint UserArrayValueObjectType_LocalizedTextValue = 1885;

        /// <remarks />
        public const uint UserArrayValueObjectType_StatusCodeValue = 1886;

        /// <remarks />
        public const uint UserArrayValueObjectType_VariantValue = 1887;

        /// <remarks />
        public const uint VectorVariableType_X = 1890;

        /// <remarks />
        public const uint VectorVariableType_Y = 1891;

        /// <remarks />
        public const uint VectorVariableType_Z = 1892;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod1_InputArguments = 1903;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod1_OutputArguments = 1904;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod2_InputArguments = 1906;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod2_OutputArguments = 1907;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod3_InputArguments = 1909;

        /// <remarks />
        public const uint MethodTestType_ScalarMethod3_OutputArguments = 1910;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod1_InputArguments = 1912;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod1_OutputArguments = 1913;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod2_InputArguments = 1915;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod2_OutputArguments = 1916;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod3_InputArguments = 1918;

        /// <remarks />
        public const uint MethodTestType_ArrayMethod3_OutputArguments = 1919;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod1_InputArguments = 1921;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod1_OutputArguments = 1922;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod2_InputArguments = 1924;

        /// <remarks />
        public const uint MethodTestType_UserScalarMethod2_OutputArguments = 1925;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod1_InputArguments = 1927;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod1_OutputArguments = 1928;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod2_InputArguments = 1930;

        /// <remarks />
        public const uint MethodTestType_UserArrayMethod2_OutputArguments = 1931;

        /// <remarks />
        public const uint TestSystemConditionType_EnabledState_Id = 1950;

        /// <remarks />
        public const uint TestSystemConditionType_Quality_SourceTimestamp = 1959;

        /// <remarks />
        public const uint TestSystemConditionType_LastSeverity_SourceTimestamp = 1961;

        /// <remarks />
        public const uint TestSystemConditionType_Comment_SourceTimestamp = 1963;

        /// <remarks />
        public const uint TestSystemConditionType_AddComment_InputArguments = 1968;

        /// <remarks />
        public const uint TestSystemConditionType_ConditionRefresh_InputArguments = 1970;

        /// <remarks />
        public const uint TestSystemConditionType_ConditionRefresh2_InputArguments = 1972;

        /// <remarks />
        public const uint TestSystemConditionType_MonitoredNodeCount = 1973;

        /// <remarks />
        public const uint Data_Static_Scalar_SimulationActive = 1977;

        /// <remarks />
        public const uint Data_Static_Scalar_GenerateValues_InputArguments = 1979;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EventId = 1981;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EventType = 1982;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_SourceNode = 1983;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_SourceName = 1984;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Time = 1985;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ReceiveTime = 1986;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Message = 1988;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Severity = 1989;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConditionClassId = 1990;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConditionClassName = 1991;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConditionName = 1994;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_BranchId = 1995;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Retain = 1996;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EnabledState = 1997;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_EnabledState_Id = 1998;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Quality = 2006;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Quality_SourceTimestamp = 2007;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_LastSeverity = 2008;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_LastSeverity_SourceTimestamp = 2009;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Comment = 2010;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Comment_SourceTimestamp = 2011;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ClientUserId = 2012;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AddComment_InputArguments = 2016;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AckedState = 2017;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_AckedState_Id = 2018;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_ConfirmedState_Id = 2027;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Acknowledge_InputArguments = 2036;

        /// <remarks />
        public const uint Data_Static_Scalar_CycleComplete_Confirm_InputArguments = 2038;

        /// <remarks />
        public const uint Data_Static_Scalar_BooleanValue = 2039;

        /// <remarks />
        public const uint Data_Static_Scalar_SByteValue = 2040;

        /// <remarks />
        public const uint Data_Static_Scalar_ByteValue = 2041;

        /// <remarks />
        public const uint Data_Static_Scalar_Int16Value = 2042;

        /// <remarks />
        public const uint Data_Static_Scalar_UInt16Value = 2043;

        /// <remarks />
        public const uint Data_Static_Scalar_Int32Value = 2044;

        /// <remarks />
        public const uint Data_Static_Scalar_UInt32Value = 2045;

        /// <remarks />
        public const uint Data_Static_Scalar_Int64Value = 2046;

        /// <remarks />
        public const uint Data_Static_Scalar_UInt64Value = 2047;

        /// <remarks />
        public const uint Data_Static_Scalar_FloatValue = 2048;

        /// <remarks />
        public const uint Data_Static_Scalar_DoubleValue = 2049;

        /// <remarks />
        public const uint Data_Static_Scalar_StringValue = 2050;

        /// <remarks />
        public const uint Data_Static_Scalar_DateTimeValue = 2051;

        /// <remarks />
        public const uint Data_Static_Scalar_GuidValue = 2052;

        /// <remarks />
        public const uint Data_Static_Scalar_ByteStringValue = 2053;

        /// <remarks />
        public const uint Data_Static_Scalar_XmlElementValue = 2054;

        /// <remarks />
        public const uint Data_Static_Scalar_NodeIdValue = 2055;

        /// <remarks />
        public const uint Data_Static_Scalar_ExpandedNodeIdValue = 2056;

        /// <remarks />
        public const uint Data_Static_Scalar_QualifiedNameValue = 2057;

        /// <remarks />
        public const uint Data_Static_Scalar_LocalizedTextValue = 2058;

        /// <remarks />
        public const uint Data_Static_Scalar_StatusCodeValue = 2059;

        /// <remarks />
        public const uint Data_Static_Scalar_VariantValue = 2060;

        /// <remarks />
        public const uint Data_Static_Scalar_EnumerationValue = 2061;

        /// <remarks />
        public const uint Data_Static_Scalar_StructureValue = 2062;

        /// <remarks />
        public const uint Data_Static_Scalar_NumberValue = 2063;

        /// <remarks />
        public const uint Data_Static_Scalar_IntegerValue = 2064;

        /// <remarks />
        public const uint Data_Static_Scalar_UIntegerValue = 2065;

        /// <remarks />
        public const uint Data_Static_Scalar_VectorValue = 2066;

        /// <remarks />
        public const uint Data_Static_Scalar_VectorValue_X = 2067;

        /// <remarks />
        public const uint Data_Static_Scalar_VectorValue_Y = 2068;

        /// <remarks />
        public const uint Data_Static_Scalar_VectorValue_Z = 2069;

        /// <remarks />
        public const uint Data_Static_Scalar_VectorUnionValue = 3586;

        /// <remarks />
        public const uint Data_Static_Scalar_VectorWithOptionalFieldsValue = 3587;

        /// <remarks />
        public const uint Data_Static_Scalar_MultipleVectorsValue = 3616;

        /// <remarks />
        public const uint Data_Static_Structure_SimulationActive = 2071;

        /// <remarks />
        public const uint Data_Static_Structure_GenerateValues_InputArguments = 2073;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_EventId = 2075;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_EventType = 2076;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_SourceNode = 2077;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_SourceName = 2078;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Time = 2079;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_ReceiveTime = 2080;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Message = 2082;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Severity = 2083;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_ConditionClassId = 2084;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_ConditionClassName = 2085;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_ConditionName = 2088;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_BranchId = 2089;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Retain = 2090;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_EnabledState = 2091;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_EnabledState_Id = 2092;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Quality = 2100;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Quality_SourceTimestamp = 2101;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_LastSeverity = 2102;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_LastSeverity_SourceTimestamp = 2103;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Comment = 2104;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Comment_SourceTimestamp = 2105;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_ClientUserId = 2106;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_AddComment_InputArguments = 2110;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_AckedState = 2111;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_AckedState_Id = 2112;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_ConfirmedState_Id = 2121;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Acknowledge_InputArguments = 2130;

        /// <remarks />
        public const uint Data_Static_Structure_CycleComplete_Confirm_InputArguments = 2132;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure = 2133;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_BooleanValue = 2134;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_SByteValue = 2135;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_ByteValue = 2136;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_Int16Value = 2137;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_UInt16Value = 2138;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_Int32Value = 2139;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_UInt32Value = 2140;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_Int64Value = 2141;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_UInt64Value = 2142;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_FloatValue = 2143;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_DoubleValue = 2144;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_StringValue = 2145;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_DateTimeValue = 2146;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_GuidValue = 2147;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_ByteStringValue = 2148;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_XmlElementValue = 2149;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_NodeIdValue = 2150;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_ExpandedNodeIdValue = 2151;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_QualifiedNameValue = 2152;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_LocalizedTextValue = 2153;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_StatusCodeValue = 2154;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_VariantValue = 2155;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_EnumerationValue = 2156;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_StructureValue = 2157;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_NumberValue = 2158;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_IntegerValue = 2159;

        /// <remarks />
        public const uint Data_Static_Structure_ScalarStructure_UIntegerValue = 2160;

        /// <remarks />
        public const uint Data_Static_Structure_VectorStructure = 2161;

        /// <remarks />
        public const uint Data_Static_Structure_VectorStructure_X = 2162;

        /// <remarks />
        public const uint Data_Static_Structure_VectorStructure_Y = 2163;

        /// <remarks />
        public const uint Data_Static_Structure_VectorStructure_Z = 2164;

        /// <remarks />
        public const uint Data_Static_Array_SimulationActive = 2166;

        /// <remarks />
        public const uint Data_Static_Array_GenerateValues_InputArguments = 2168;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EventId = 2170;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EventType = 2171;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_SourceNode = 2172;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_SourceName = 2173;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Time = 2174;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ReceiveTime = 2175;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Message = 2177;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Severity = 2178;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConditionClassId = 2179;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConditionClassName = 2180;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConditionName = 2183;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_BranchId = 2184;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Retain = 2185;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EnabledState = 2186;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_EnabledState_Id = 2187;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Quality = 2195;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Quality_SourceTimestamp = 2196;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_LastSeverity = 2197;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_LastSeverity_SourceTimestamp = 2198;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Comment = 2199;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Comment_SourceTimestamp = 2200;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ClientUserId = 2201;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AddComment_InputArguments = 2205;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AckedState = 2206;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_AckedState_Id = 2207;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_ConfirmedState_Id = 2216;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Acknowledge_InputArguments = 2225;

        /// <remarks />
        public const uint Data_Static_Array_CycleComplete_Confirm_InputArguments = 2227;

        /// <remarks />
        public const uint Data_Static_Array_BooleanValue = 2228;

        /// <remarks />
        public const uint Data_Static_Array_SByteValue = 2229;

        /// <remarks />
        public const uint Data_Static_Array_ByteValue = 2230;

        /// <remarks />
        public const uint Data_Static_Array_Int16Value = 2231;

        /// <remarks />
        public const uint Data_Static_Array_UInt16Value = 2232;

        /// <remarks />
        public const uint Data_Static_Array_Int32Value = 2233;

        /// <remarks />
        public const uint Data_Static_Array_UInt32Value = 2234;

        /// <remarks />
        public const uint Data_Static_Array_Int64Value = 2235;

        /// <remarks />
        public const uint Data_Static_Array_UInt64Value = 2236;

        /// <remarks />
        public const uint Data_Static_Array_FloatValue = 2237;

        /// <remarks />
        public const uint Data_Static_Array_DoubleValue = 2238;

        /// <remarks />
        public const uint Data_Static_Array_StringValue = 2239;

        /// <remarks />
        public const uint Data_Static_Array_DateTimeValue = 2240;

        /// <remarks />
        public const uint Data_Static_Array_GuidValue = 2241;

        /// <remarks />
        public const uint Data_Static_Array_ByteStringValue = 2242;

        /// <remarks />
        public const uint Data_Static_Array_XmlElementValue = 2243;

        /// <remarks />
        public const uint Data_Static_Array_NodeIdValue = 2244;

        /// <remarks />
        public const uint Data_Static_Array_ExpandedNodeIdValue = 2245;

        /// <remarks />
        public const uint Data_Static_Array_QualifiedNameValue = 2246;

        /// <remarks />
        public const uint Data_Static_Array_LocalizedTextValue = 2247;

        /// <remarks />
        public const uint Data_Static_Array_StatusCodeValue = 2248;

        /// <remarks />
        public const uint Data_Static_Array_VariantValue = 2249;

        /// <remarks />
        public const uint Data_Static_Array_EnumerationValue = 2250;

        /// <remarks />
        public const uint Data_Static_Array_StructureValue = 2251;

        /// <remarks />
        public const uint Data_Static_Array_NumberValue = 2252;

        /// <remarks />
        public const uint Data_Static_Array_IntegerValue = 2253;

        /// <remarks />
        public const uint Data_Static_Array_UIntegerValue = 2254;

        /// <remarks />
        public const uint Data_Static_Array_VectorValue = 2255;

        /// <remarks />
        public const uint Data_Static_Array_VectorUnionValue = 3610;

        /// <remarks />
        public const uint Data_Static_Array_VectorWithOptionalFieldsValue = 3611;

        /// <remarks />
        public const uint Data_Static_Array_MultipleVectorsValue = 3628;

        /// <remarks />
        public const uint Data_Static_UserScalar_SimulationActive = 2257;

        /// <remarks />
        public const uint Data_Static_UserScalar_GenerateValues_InputArguments = 2259;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EventId = 2261;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EventType = 2262;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_SourceNode = 2263;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_SourceName = 2264;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Time = 2265;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ReceiveTime = 2266;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Message = 2268;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Severity = 2269;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConditionClassId = 2270;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConditionClassName = 2271;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConditionName = 2274;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_BranchId = 2275;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Retain = 2276;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EnabledState = 2277;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_EnabledState_Id = 2278;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Quality = 2286;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Quality_SourceTimestamp = 2287;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_LastSeverity = 2288;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = 2289;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Comment = 2290;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Comment_SourceTimestamp = 2291;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ClientUserId = 2292;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AddComment_InputArguments = 2296;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AckedState = 2297;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_AckedState_Id = 2298;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_ConfirmedState_Id = 2307;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Acknowledge_InputArguments = 2316;

        /// <remarks />
        public const uint Data_Static_UserScalar_CycleComplete_Confirm_InputArguments = 2318;

        /// <remarks />
        public const uint Data_Static_UserScalar_BooleanValue = 2319;

        /// <remarks />
        public const uint Data_Static_UserScalar_SByteValue = 2320;

        /// <remarks />
        public const uint Data_Static_UserScalar_ByteValue = 2321;

        /// <remarks />
        public const uint Data_Static_UserScalar_Int16Value = 2322;

        /// <remarks />
        public const uint Data_Static_UserScalar_UInt16Value = 2323;

        /// <remarks />
        public const uint Data_Static_UserScalar_Int32Value = 2324;

        /// <remarks />
        public const uint Data_Static_UserScalar_UInt32Value = 2325;

        /// <remarks />
        public const uint Data_Static_UserScalar_Int64Value = 2326;

        /// <remarks />
        public const uint Data_Static_UserScalar_UInt64Value = 2327;

        /// <remarks />
        public const uint Data_Static_UserScalar_FloatValue = 2328;

        /// <remarks />
        public const uint Data_Static_UserScalar_DoubleValue = 2329;

        /// <remarks />
        public const uint Data_Static_UserScalar_StringValue = 2330;

        /// <remarks />
        public const uint Data_Static_UserScalar_DateTimeValue = 2331;

        /// <remarks />
        public const uint Data_Static_UserScalar_GuidValue = 2332;

        /// <remarks />
        public const uint Data_Static_UserScalar_ByteStringValue = 2333;

        /// <remarks />
        public const uint Data_Static_UserScalar_XmlElementValue = 2334;

        /// <remarks />
        public const uint Data_Static_UserScalar_NodeIdValue = 2335;

        /// <remarks />
        public const uint Data_Static_UserScalar_ExpandedNodeIdValue = 2336;

        /// <remarks />
        public const uint Data_Static_UserScalar_QualifiedNameValue = 2337;

        /// <remarks />
        public const uint Data_Static_UserScalar_LocalizedTextValue = 2338;

        /// <remarks />
        public const uint Data_Static_UserScalar_StatusCodeValue = 2339;

        /// <remarks />
        public const uint Data_Static_UserScalar_VariantValue = 2340;

        /// <remarks />
        public const uint Data_Static_UserArray_SimulationActive = 2342;

        /// <remarks />
        public const uint Data_Static_UserArray_GenerateValues_InputArguments = 2344;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EventId = 2346;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EventType = 2347;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_SourceNode = 2348;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_SourceName = 2349;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Time = 2350;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ReceiveTime = 2351;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Message = 2353;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Severity = 2354;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConditionClassId = 2355;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConditionClassName = 2356;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConditionName = 2359;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_BranchId = 2360;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Retain = 2361;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EnabledState = 2362;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_EnabledState_Id = 2363;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Quality = 2371;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Quality_SourceTimestamp = 2372;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_LastSeverity = 2373;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_LastSeverity_SourceTimestamp = 2374;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Comment = 2375;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Comment_SourceTimestamp = 2376;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ClientUserId = 2377;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AddComment_InputArguments = 2381;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AckedState = 2382;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_AckedState_Id = 2383;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_ConfirmedState_Id = 2392;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Acknowledge_InputArguments = 2401;

        /// <remarks />
        public const uint Data_Static_UserArray_CycleComplete_Confirm_InputArguments = 2403;

        /// <remarks />
        public const uint Data_Static_UserArray_BooleanValue = 2404;

        /// <remarks />
        public const uint Data_Static_UserArray_SByteValue = 2405;

        /// <remarks />
        public const uint Data_Static_UserArray_ByteValue = 2406;

        /// <remarks />
        public const uint Data_Static_UserArray_Int16Value = 2407;

        /// <remarks />
        public const uint Data_Static_UserArray_UInt16Value = 2408;

        /// <remarks />
        public const uint Data_Static_UserArray_Int32Value = 2409;

        /// <remarks />
        public const uint Data_Static_UserArray_UInt32Value = 2410;

        /// <remarks />
        public const uint Data_Static_UserArray_Int64Value = 2411;

        /// <remarks />
        public const uint Data_Static_UserArray_UInt64Value = 2412;

        /// <remarks />
        public const uint Data_Static_UserArray_FloatValue = 2413;

        /// <remarks />
        public const uint Data_Static_UserArray_DoubleValue = 2414;

        /// <remarks />
        public const uint Data_Static_UserArray_StringValue = 2415;

        /// <remarks />
        public const uint Data_Static_UserArray_DateTimeValue = 2416;

        /// <remarks />
        public const uint Data_Static_UserArray_GuidValue = 2417;

        /// <remarks />
        public const uint Data_Static_UserArray_ByteStringValue = 2418;

        /// <remarks />
        public const uint Data_Static_UserArray_XmlElementValue = 2419;

        /// <remarks />
        public const uint Data_Static_UserArray_NodeIdValue = 2420;

        /// <remarks />
        public const uint Data_Static_UserArray_ExpandedNodeIdValue = 2421;

        /// <remarks />
        public const uint Data_Static_UserArray_QualifiedNameValue = 2422;

        /// <remarks />
        public const uint Data_Static_UserArray_LocalizedTextValue = 2423;

        /// <remarks />
        public const uint Data_Static_UserArray_StatusCodeValue = 2424;

        /// <remarks />
        public const uint Data_Static_UserArray_VariantValue = 2425;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_SimulationActive = 2427;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_GenerateValues_InputArguments = 2429;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EventId = 2431;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EventType = 2432;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_SourceNode = 2433;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_SourceName = 2434;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Time = 2435;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ReceiveTime = 2436;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Message = 2438;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Severity = 2439;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionClassId = 2440;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionClassName = 2441;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionName = 2444;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_BranchId = 2445;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Retain = 2446;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EnabledState = 2447;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_EnabledState_Id = 2448;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Quality = 2456;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Quality_SourceTimestamp = 2457;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_LastSeverity = 2458;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = 2459;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Comment = 2460;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Comment_SourceTimestamp = 2461;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ClientUserId = 2462;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AddComment_InputArguments = 2466;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AckedState = 2467;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_AckedState_Id = 2468;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_ConfirmedState_Id = 2477;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Acknowledge_InputArguments = 2486;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_CycleComplete_Confirm_InputArguments = 2488;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_SByteValue = 2489;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_SByteValue_EURange = 2493;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_ByteValue = 2495;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_ByteValue_EURange = 2499;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int16Value = 2501;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int16Value_EURange = 2505;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt16Value = 2507;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt16Value_EURange = 2511;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int32Value = 2513;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int32Value_EURange = 2517;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt32Value = 2519;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt32Value_EURange = 2523;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int64Value = 2525;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_Int64Value_EURange = 2529;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt64Value = 2531;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UInt64Value_EURange = 2535;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_FloatValue = 2537;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_FloatValue_EURange = 2541;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_DoubleValue = 2543;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_DoubleValue_EURange = 2547;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_NumberValue = 2549;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_NumberValue_EURange = 2553;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_IntegerValue = 2555;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_IntegerValue_EURange = 2559;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UIntegerValue = 2561;

        /// <remarks />
        public const uint Data_Static_AnalogScalar_UIntegerValue_EURange = 2565;

        /// <remarks />
        public const uint Data_Static_AnalogArray_SimulationActive = 2568;

        /// <remarks />
        public const uint Data_Static_AnalogArray_GenerateValues_InputArguments = 2570;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EventId = 2572;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EventType = 2573;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_SourceNode = 2574;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_SourceName = 2575;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Time = 2576;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ReceiveTime = 2577;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Message = 2579;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Severity = 2580;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionClassId = 2581;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionClassName = 2582;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionName = 2585;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_BranchId = 2586;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Retain = 2587;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EnabledState = 2588;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_EnabledState_Id = 2589;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Quality = 2597;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Quality_SourceTimestamp = 2598;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_LastSeverity = 2599;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = 2600;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Comment = 2601;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Comment_SourceTimestamp = 2602;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ClientUserId = 2603;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AddComment_InputArguments = 2607;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AckedState = 2608;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_AckedState_Id = 2609;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_ConfirmedState_Id = 2618;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Acknowledge_InputArguments = 2627;

        /// <remarks />
        public const uint Data_Static_AnalogArray_CycleComplete_Confirm_InputArguments = 2629;

        /// <remarks />
        public const uint Data_Static_AnalogArray_SByteValue = 2630;

        /// <remarks />
        public const uint Data_Static_AnalogArray_SByteValue_EURange = 2634;

        /// <remarks />
        public const uint Data_Static_AnalogArray_ByteValue = 2636;

        /// <remarks />
        public const uint Data_Static_AnalogArray_ByteValue_EURange = 2640;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int16Value = 2642;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int16Value_EURange = 2646;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt16Value = 2648;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt16Value_EURange = 2652;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int32Value = 2654;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int32Value_EURange = 2658;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt32Value = 2660;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt32Value_EURange = 2664;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int64Value = 2666;

        /// <remarks />
        public const uint Data_Static_AnalogArray_Int64Value_EURange = 2670;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt64Value = 2672;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UInt64Value_EURange = 2676;

        /// <remarks />
        public const uint Data_Static_AnalogArray_FloatValue = 2678;

        /// <remarks />
        public const uint Data_Static_AnalogArray_FloatValue_EURange = 2682;

        /// <remarks />
        public const uint Data_Static_AnalogArray_DoubleValue = 2684;

        /// <remarks />
        public const uint Data_Static_AnalogArray_DoubleValue_EURange = 2688;

        /// <remarks />
        public const uint Data_Static_AnalogArray_NumberValue = 2690;

        /// <remarks />
        public const uint Data_Static_AnalogArray_NumberValue_EURange = 2694;

        /// <remarks />
        public const uint Data_Static_AnalogArray_IntegerValue = 2696;

        /// <remarks />
        public const uint Data_Static_AnalogArray_IntegerValue_EURange = 2700;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UIntegerValue = 2702;

        /// <remarks />
        public const uint Data_Static_AnalogArray_UIntegerValue_EURange = 2706;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod1_InputArguments = 2710;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod1_OutputArguments = 2711;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod2_InputArguments = 2713;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod2_OutputArguments = 2714;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod3_InputArguments = 2716;

        /// <remarks />
        public const uint Data_Static_MethodTest_ScalarMethod3_OutputArguments = 2717;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod1_InputArguments = 2719;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod1_OutputArguments = 2720;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod2_InputArguments = 2722;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod2_OutputArguments = 2723;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod3_InputArguments = 2725;

        /// <remarks />
        public const uint Data_Static_MethodTest_ArrayMethod3_OutputArguments = 2726;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod1_InputArguments = 2728;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod1_OutputArguments = 2729;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod2_InputArguments = 2731;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserScalarMethod2_OutputArguments = 2732;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod1_InputArguments = 2734;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod1_OutputArguments = 2735;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod2_InputArguments = 2737;

        /// <remarks />
        public const uint Data_Static_MethodTest_UserArrayMethod2_OutputArguments = 2738;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_SimulationActive = 2741;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_GenerateValues_InputArguments = 2743;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EventId = 2745;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EventType = 2746;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_SourceNode = 2747;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_SourceName = 2748;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Time = 2749;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ReceiveTime = 2750;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Message = 2752;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Severity = 2753;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionClassId = 2754;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionClassName = 2755;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionName = 2758;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_BranchId = 2759;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Retain = 2760;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EnabledState = 2761;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_EnabledState_Id = 2762;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Quality = 2770;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Quality_SourceTimestamp = 2771;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_LastSeverity = 2772;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_LastSeverity_SourceTimestamp = 2773;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Comment = 2774;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Comment_SourceTimestamp = 2775;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ClientUserId = 2776;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AddComment_InputArguments = 2780;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AckedState = 2781;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_AckedState_Id = 2782;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_ConfirmedState_Id = 2791;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Acknowledge_InputArguments = 2800;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_CycleComplete_Confirm_InputArguments = 2802;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_BooleanValue = 2803;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_SByteValue = 2804;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_ByteValue = 2805;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_Int16Value = 2806;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UInt16Value = 2807;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_Int32Value = 2808;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UInt32Value = 2809;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_Int64Value = 2810;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UInt64Value = 2811;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_FloatValue = 2812;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_DoubleValue = 2813;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_StringValue = 2814;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_DateTimeValue = 2815;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_GuidValue = 2816;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_ByteStringValue = 2817;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_XmlElementValue = 2818;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_NodeIdValue = 2819;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_ExpandedNodeIdValue = 2820;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_QualifiedNameValue = 2821;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_LocalizedTextValue = 2822;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_StatusCodeValue = 2823;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VariantValue = 2824;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_EnumerationValue = 2825;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_StructureValue = 2826;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_NumberValue = 2827;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_IntegerValue = 2828;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_UIntegerValue = 2829;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VectorValue = 2830;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VectorValue_X = 2831;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VectorValue_Y = 2832;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VectorValue_Z = 2833;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VectorUnionValue = 3588;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_VectorWithOptionalFieldsValue = 3589;

        /// <remarks />
        public const uint Data_Dynamic_Scalar_MultipleVectorsValue = 3617;

        /// <remarks />
        public const uint Data_Dynamic_Structure_SimulationActive = 2835;

        /// <remarks />
        public const uint Data_Dynamic_Structure_GenerateValues_InputArguments = 2837;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_EventId = 2839;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_EventType = 2840;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_SourceNode = 2841;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_SourceName = 2842;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Time = 2843;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_ReceiveTime = 2844;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Message = 2846;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Severity = 2847;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_ConditionClassId = 2848;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_ConditionClassName = 2849;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_ConditionName = 2852;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_BranchId = 2853;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Retain = 2854;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_EnabledState = 2855;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_EnabledState_Id = 2856;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Quality = 2864;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Quality_SourceTimestamp = 2865;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_LastSeverity = 2866;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_LastSeverity_SourceTimestamp = 2867;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Comment = 2868;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Comment_SourceTimestamp = 2869;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_ClientUserId = 2870;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_AddComment_InputArguments = 2874;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_AckedState = 2875;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_AckedState_Id = 2876;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_ConfirmedState_Id = 2885;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Acknowledge_InputArguments = 2894;

        /// <remarks />
        public const uint Data_Dynamic_Structure_CycleComplete_Confirm_InputArguments = 2896;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure = 2897;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_BooleanValue = 2898;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_SByteValue = 2899;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_ByteValue = 2900;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_Int16Value = 2901;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_UInt16Value = 2902;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_Int32Value = 2903;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_UInt32Value = 2904;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_Int64Value = 2905;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_UInt64Value = 2906;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_FloatValue = 2907;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_DoubleValue = 2908;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_StringValue = 2909;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_DateTimeValue = 2910;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_GuidValue = 2911;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_ByteStringValue = 2912;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_XmlElementValue = 2913;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_NodeIdValue = 2914;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_ExpandedNodeIdValue = 2915;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_QualifiedNameValue = 2916;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_LocalizedTextValue = 2917;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_StatusCodeValue = 2918;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_VariantValue = 2919;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_EnumerationValue = 2920;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_StructureValue = 2921;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_NumberValue = 2922;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_IntegerValue = 2923;

        /// <remarks />
        public const uint Data_Dynamic_Structure_ScalarStructure_UIntegerValue = 2924;

        /// <remarks />
        public const uint Data_Dynamic_Structure_VectorStructure = 2925;

        /// <remarks />
        public const uint Data_Dynamic_Structure_VectorStructure_X = 2926;

        /// <remarks />
        public const uint Data_Dynamic_Structure_VectorStructure_Y = 2927;

        /// <remarks />
        public const uint Data_Dynamic_Structure_VectorStructure_Z = 2928;

        /// <remarks />
        public const uint Data_Dynamic_Array_SimulationActive = 2930;

        /// <remarks />
        public const uint Data_Dynamic_Array_GenerateValues_InputArguments = 2932;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EventId = 2934;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EventType = 2935;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_SourceNode = 2936;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_SourceName = 2937;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Time = 2938;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ReceiveTime = 2939;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Message = 2941;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Severity = 2942;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConditionClassId = 2943;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConditionClassName = 2944;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConditionName = 2947;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_BranchId = 2948;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Retain = 2949;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EnabledState = 2950;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_EnabledState_Id = 2951;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Quality = 2959;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Quality_SourceTimestamp = 2960;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_LastSeverity = 2961;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_LastSeverity_SourceTimestamp = 2962;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Comment = 2963;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Comment_SourceTimestamp = 2964;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ClientUserId = 2965;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AddComment_InputArguments = 2969;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AckedState = 2970;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_AckedState_Id = 2971;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_ConfirmedState_Id = 2980;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Acknowledge_InputArguments = 2989;

        /// <remarks />
        public const uint Data_Dynamic_Array_CycleComplete_Confirm_InputArguments = 2991;

        /// <remarks />
        public const uint Data_Dynamic_Array_BooleanValue = 2992;

        /// <remarks />
        public const uint Data_Dynamic_Array_SByteValue = 2993;

        /// <remarks />
        public const uint Data_Dynamic_Array_ByteValue = 2994;

        /// <remarks />
        public const uint Data_Dynamic_Array_Int16Value = 2995;

        /// <remarks />
        public const uint Data_Dynamic_Array_UInt16Value = 2996;

        /// <remarks />
        public const uint Data_Dynamic_Array_Int32Value = 2997;

        /// <remarks />
        public const uint Data_Dynamic_Array_UInt32Value = 2998;

        /// <remarks />
        public const uint Data_Dynamic_Array_Int64Value = 2999;

        /// <remarks />
        public const uint Data_Dynamic_Array_UInt64Value = 3000;

        /// <remarks />
        public const uint Data_Dynamic_Array_FloatValue = 3001;

        /// <remarks />
        public const uint Data_Dynamic_Array_DoubleValue = 3002;

        /// <remarks />
        public const uint Data_Dynamic_Array_StringValue = 3003;

        /// <remarks />
        public const uint Data_Dynamic_Array_DateTimeValue = 3004;

        /// <remarks />
        public const uint Data_Dynamic_Array_GuidValue = 3005;

        /// <remarks />
        public const uint Data_Dynamic_Array_ByteStringValue = 3006;

        /// <remarks />
        public const uint Data_Dynamic_Array_XmlElementValue = 3007;

        /// <remarks />
        public const uint Data_Dynamic_Array_NodeIdValue = 3008;

        /// <remarks />
        public const uint Data_Dynamic_Array_ExpandedNodeIdValue = 3009;

        /// <remarks />
        public const uint Data_Dynamic_Array_QualifiedNameValue = 3010;

        /// <remarks />
        public const uint Data_Dynamic_Array_LocalizedTextValue = 3011;

        /// <remarks />
        public const uint Data_Dynamic_Array_StatusCodeValue = 3012;

        /// <remarks />
        public const uint Data_Dynamic_Array_VariantValue = 3013;

        /// <remarks />
        public const uint Data_Dynamic_Array_EnumerationValue = 3014;

        /// <remarks />
        public const uint Data_Dynamic_Array_StructureValue = 3015;

        /// <remarks />
        public const uint Data_Dynamic_Array_NumberValue = 3016;

        /// <remarks />
        public const uint Data_Dynamic_Array_IntegerValue = 3017;

        /// <remarks />
        public const uint Data_Dynamic_Array_UIntegerValue = 3018;

        /// <remarks />
        public const uint Data_Dynamic_Array_VectorValue = 3019;

        /// <remarks />
        public const uint Data_Dynamic_Array_VectorUnionValue = 3612;

        /// <remarks />
        public const uint Data_Dynamic_Array_VectorWithOptionalFieldsValue = 3613;

        /// <remarks />
        public const uint Data_Dynamic_Array_MultipleVectorsValue = 3629;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_SimulationActive = 3021;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_GenerateValues_InputArguments = 3023;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EventId = 3025;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EventType = 3026;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_SourceNode = 3027;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_SourceName = 3028;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Time = 3029;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ReceiveTime = 3030;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Message = 3032;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Severity = 3033;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionClassId = 3034;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionClassName = 3035;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionName = 3038;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_BranchId = 3039;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Retain = 3040;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EnabledState = 3041;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_EnabledState_Id = 3042;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Quality = 3050;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Quality_SourceTimestamp = 3051;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_LastSeverity = 3052;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = 3053;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Comment = 3054;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Comment_SourceTimestamp = 3055;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ClientUserId = 3056;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AddComment_InputArguments = 3060;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AckedState = 3061;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_AckedState_Id = 3062;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConfirmedState_Id = 3071;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Acknowledge_InputArguments = 3080;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_CycleComplete_Confirm_InputArguments = 3082;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_BooleanValue = 3083;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_SByteValue = 3084;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_ByteValue = 3085;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_Int16Value = 3086;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_UInt16Value = 3087;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_Int32Value = 3088;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_UInt32Value = 3089;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_Int64Value = 3090;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_UInt64Value = 3091;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_FloatValue = 3092;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_DoubleValue = 3093;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_StringValue = 3094;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_DateTimeValue = 3095;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_GuidValue = 3096;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_ByteStringValue = 3097;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_XmlElementValue = 3098;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_NodeIdValue = 3099;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_ExpandedNodeIdValue = 3100;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_QualifiedNameValue = 3101;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_LocalizedTextValue = 3102;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_StatusCodeValue = 3103;

        /// <remarks />
        public const uint Data_Dynamic_UserScalar_VariantValue = 3104;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_SimulationActive = 3106;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_GenerateValues_InputArguments = 3108;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EventId = 3110;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EventType = 3111;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_SourceNode = 3112;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_SourceName = 3113;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Time = 3114;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ReceiveTime = 3115;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Message = 3117;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Severity = 3118;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionClassId = 3119;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionClassName = 3120;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionName = 3123;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_BranchId = 3124;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Retain = 3125;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EnabledState = 3126;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_EnabledState_Id = 3127;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Quality = 3135;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Quality_SourceTimestamp = 3136;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_LastSeverity = 3137;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_LastSeverity_SourceTimestamp = 3138;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Comment = 3139;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Comment_SourceTimestamp = 3140;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ClientUserId = 3141;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AddComment_InputArguments = 3145;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AckedState = 3146;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_AckedState_Id = 3147;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_ConfirmedState_Id = 3156;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Acknowledge_InputArguments = 3165;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_CycleComplete_Confirm_InputArguments = 3167;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_BooleanValue = 3168;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_SByteValue = 3169;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_ByteValue = 3170;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_Int16Value = 3171;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_UInt16Value = 3172;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_Int32Value = 3173;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_UInt32Value = 3174;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_Int64Value = 3175;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_UInt64Value = 3176;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_FloatValue = 3177;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_DoubleValue = 3178;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_StringValue = 3179;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_DateTimeValue = 3180;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_GuidValue = 3181;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_ByteStringValue = 3182;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_XmlElementValue = 3183;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_NodeIdValue = 3184;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_ExpandedNodeIdValue = 3185;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_QualifiedNameValue = 3186;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_LocalizedTextValue = 3187;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_StatusCodeValue = 3188;

        /// <remarks />
        public const uint Data_Dynamic_UserArray_VariantValue = 3189;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_SimulationActive = 3191;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_GenerateValues_InputArguments = 3193;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EventId = 3195;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EventType = 3196;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_SourceNode = 3197;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_SourceName = 3198;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Time = 3199;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ReceiveTime = 3200;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Message = 3202;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Severity = 3203;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassId = 3204;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassName = 3205;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionName = 3208;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_BranchId = 3209;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Retain = 3210;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EnabledState = 3211;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EnabledState_Id = 3212;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Quality = 3220;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Quality_SourceTimestamp = 3221;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity = 3222;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = 3223;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Comment = 3224;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Comment_SourceTimestamp = 3225;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ClientUserId = 3226;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AddComment_InputArguments = 3230;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AckedState = 3231;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AckedState_Id = 3232;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConfirmedState_Id = 3241;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge_InputArguments = 3250;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Confirm_InputArguments = 3252;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_SByteValue = 3253;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_SByteValue_EURange = 3257;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_ByteValue = 3259;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_ByteValue_EURange = 3263;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int16Value = 3265;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int16Value_EURange = 3269;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt16Value = 3271;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt16Value_EURange = 3275;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int32Value = 3277;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int32Value_EURange = 3281;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt32Value = 3283;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt32Value_EURange = 3287;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int64Value = 3289;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_Int64Value_EURange = 3293;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt64Value = 3295;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UInt64Value_EURange = 3299;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_FloatValue = 3301;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_FloatValue_EURange = 3305;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_DoubleValue = 3307;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_DoubleValue_EURange = 3311;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_NumberValue = 3313;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_NumberValue_EURange = 3317;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_IntegerValue = 3319;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_IntegerValue_EURange = 3323;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UIntegerValue = 3325;

        /// <remarks />
        public const uint Data_Dynamic_AnalogScalar_UIntegerValue_EURange = 3329;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_SimulationActive = 3332;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_GenerateValues_InputArguments = 3334;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EventId = 3336;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EventType = 3337;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_SourceNode = 3338;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_SourceName = 3339;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Time = 3340;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ReceiveTime = 3341;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Message = 3343;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Severity = 3344;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionClassId = 3345;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionClassName = 3346;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionName = 3349;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_BranchId = 3350;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Retain = 3351;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EnabledState = 3352;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EnabledState_Id = 3353;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Quality = 3361;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Quality_SourceTimestamp = 3362;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_LastSeverity = 3363;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = 3364;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Comment = 3365;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Comment_SourceTimestamp = 3366;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ClientUserId = 3367;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AddComment_InputArguments = 3371;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AckedState = 3372;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AckedState_Id = 3373;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConfirmedState_Id = 3382;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Acknowledge_InputArguments = 3391;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Confirm_InputArguments = 3393;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_SByteValue = 3394;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_SByteValue_EURange = 3398;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_ByteValue = 3400;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_ByteValue_EURange = 3404;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int16Value = 3406;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int16Value_EURange = 3410;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt16Value = 3412;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt16Value_EURange = 3416;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int32Value = 3418;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int32Value_EURange = 3422;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt32Value = 3424;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt32Value_EURange = 3428;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int64Value = 3430;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_Int64Value_EURange = 3434;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt64Value = 3436;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UInt64Value_EURange = 3440;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_FloatValue = 3442;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_FloatValue_EURange = 3446;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_DoubleValue = 3448;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_DoubleValue_EURange = 3452;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_NumberValue = 3454;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_NumberValue_EURange = 3458;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_IntegerValue = 3460;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_IntegerValue_EURange = 3464;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UIntegerValue = 3466;

        /// <remarks />
        public const uint Data_Dynamic_AnalogArray_UIntegerValue_EURange = 3470;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EventId = 3474;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EventType = 3475;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_SourceNode = 3476;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_SourceName = 3477;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Time = 3478;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ReceiveTime = 3479;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Message = 3481;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Severity = 3482;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ConditionClassId = 3483;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ConditionClassName = 3484;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ConditionName = 3487;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_BranchId = 3488;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Retain = 3489;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EnabledState = 3490;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_EnabledState_Id = 3491;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Quality = 3499;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Quality_SourceTimestamp = 3500;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_LastSeverity = 3501;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_LastSeverity_SourceTimestamp = 3502;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Comment = 3503;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_Comment_SourceTimestamp = 3504;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_ClientUserId = 3505;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_AddComment_InputArguments = 3509;

        /// <remarks />
        public const uint Data_Conditions_SystemStatus_MonitoredNodeCount = 3510;

        /// <remarks />
        public const uint TestData_BinarySchema = 3518;

        /// <remarks />
        public const uint TestData_BinarySchema_NamespaceUri = 3520;

        /// <remarks />
        public const uint TestData_BinarySchema_Deprecated = 3521;

        /// <remarks />
        public const uint TestData_BinarySchema_ScalarStructureDataType = 3522;

        /// <remarks />
        public const uint TestData_BinarySchema_ArrayValueDataType = 3525;

        /// <remarks />
        public const uint TestData_BinarySchema_UserScalarValueDataType = 3528;

        /// <remarks />
        public const uint TestData_BinarySchema_UserArrayValueDataType = 3531;

        /// <remarks />
        public const uint TestData_BinarySchema_Vector = 3534;

        /// <remarks />
        public const uint TestData_BinarySchema_VectorUnion = 3592;

        /// <remarks />
        public const uint TestData_BinarySchema_VectorWithOptionalFields = 3595;

        /// <remarks />
        public const uint TestData_BinarySchema_MultipleVectors = 3619;

        /// <remarks />
        public const uint TestData_BinarySchema_WorkOrderStatusType = 3537;

        /// <remarks />
        public const uint TestData_BinarySchema_WorkOrderType = 3540;

        /// <remarks />
        public const uint TestData_XmlSchema = 3550;

        /// <remarks />
        public const uint TestData_XmlSchema_NamespaceUri = 3552;

        /// <remarks />
        public const uint TestData_XmlSchema_Deprecated = 3553;

        /// <remarks />
        public const uint TestData_XmlSchema_ScalarStructureDataType = 3554;

        /// <remarks />
        public const uint TestData_XmlSchema_ArrayValueDataType = 3557;

        /// <remarks />
        public const uint TestData_XmlSchema_UserScalarValueDataType = 3560;

        /// <remarks />
        public const uint TestData_XmlSchema_UserArrayValueDataType = 3563;

        /// <remarks />
        public const uint TestData_XmlSchema_Vector = 3566;

        /// <remarks />
        public const uint TestData_XmlSchema_VectorUnion = 3600;

        /// <remarks />
        public const uint TestData_XmlSchema_VectorWithOptionalFields = 3603;

        /// <remarks />
        public const uint TestData_XmlSchema_MultipleVectors = 3623;

        /// <remarks />
        public const uint TestData_XmlSchema_WorkOrderStatusType = 3569;

        /// <remarks />
        public const uint TestData_XmlSchema_WorkOrderType = 3572;
    }
    #endregion

    #region VariableType Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class VariableTypes
    {
        /// <remarks />
        public const uint ScalarStructureVariableType = 1079;

        /// <remarks />
        public const uint VectorVariableType = 1889;
    }
    #endregion

    #region DataType Node Identifiers
    /// <remarks />
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class DataTypeIds
    {
        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureDataType = new ExpandedNodeId(TestData.DataTypes.ScalarStructureDataType, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId VectorUnion = new ExpandedNodeId(TestData.DataTypes.VectorUnion, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId VectorWithOptionalFields = new ExpandedNodeId(TestData.DataTypes.VectorWithOptionalFields, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MultipleVectors = new ExpandedNodeId(TestData.DataTypes.MultipleVectors, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.ScalarValueObjectType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.ScalarValueObjectType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.ScalarValueObjectType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.ScalarValueObjectType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.StructureValueObjectType_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.StructureValueObjectType_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.StructureValueObjectType_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.StructureValueObjectType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Static_Structure_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_Structure_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_Structure_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_Structure_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_Structure_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_Structure_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Dynamic_Structure_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Structure_GenerateValues, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Structure_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Structure_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Structure_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Structure_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data = new ExpandedNodeId(TestData.Objects.Data, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static = new ExpandedNodeId(TestData.Objects.Data_Static, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar = new ExpandedNodeId(TestData.Objects.Data_Static_Scalar, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_Scalar_CycleComplete, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure = new ExpandedNodeId(TestData.Objects.Data_Static_Structure, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_Structure_CycleComplete, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Dynamic_Structure = new ExpandedNodeId(TestData.Objects.Data_Dynamic_Structure, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_Structure_CycleComplete, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId ScalarStructureDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.ScalarStructureDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.ArrayValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.UserScalarValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.UserArrayValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Vector_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.Vector_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId VectorUnion_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.VectorUnion_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId VectorWithOptionalFields_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.VectorWithOptionalFields_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MultipleVectors_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.MultipleVectors_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderStatusType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.WorkOrderStatusType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.WorkOrderType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.ScalarStructureDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.ArrayValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.UserScalarValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.UserArrayValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Vector_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.Vector_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId VectorUnion_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.VectorUnion_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId VectorWithOptionalFields_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.VectorWithOptionalFields_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MultipleVectors_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.MultipleVectors_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderStatusType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.WorkOrderStatusType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId WorkOrderType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.WorkOrderType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureDataType_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.ScalarStructureDataType_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueDataType_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.ArrayValueDataType_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserScalarValueDataType_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.UserScalarValueDataType_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId UserArrayValueDataType_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.UserArrayValueDataType_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Vector_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.Vector_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId VectorUnion_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.VectorUnion_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId VectorWithOptionalFields_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.VectorWithOptionalFields_Encoding_DefaultJson, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId MultipleVectors_Encoding_DefaultJson = new ExpandedNodeId(TestData.Objects.MultipleVectors_Encoding_DefaultJson, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId StructureValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.StructureValueObjectType, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId ScalarStructureVariableType_BooleanValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_SByteValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_ByteValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_Int16Value = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_UInt16Value = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_Int32Value = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_UInt32Value = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_Int64Value = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_UInt64Value = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_FloatValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_DoubleValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_StringValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_DateTimeValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_GuidValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_ByteStringValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_XmlElementValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_NodeIdValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_StatusCodeValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_VariantValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_EnumerationValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_StructureValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_NumberValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_IntegerValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarStructureVariableType_UIntegerValue = new ExpandedNodeId(TestData.Variables.ScalarStructureVariableType_UIntegerValue, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId ScalarValueObjectType_VectorUnionValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_VectorUnionValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_VectorWithOptionalFieldsValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_VectorWithOptionalFieldsValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ScalarValueObjectType_MultipleVectorsValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_MultipleVectorsValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_BooleanValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_SByteValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_ByteValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_Int16Value = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_UInt16Value = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_Int32Value = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_UInt32Value = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_Int64Value = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_UInt64Value = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_FloatValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_DoubleValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_StringValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_DateTimeValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_GuidValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_ByteStringValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_XmlElementValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_NodeIdValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_StatusCodeValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_VariantValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_EnumerationValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_StructureValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_NumberValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_IntegerValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_ScalarStructure_UIntegerValue = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_ScalarStructure_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_VectorStructure = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_VectorStructure, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_VectorStructure_X = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_VectorStructure_X, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_VectorStructure_Y = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_VectorStructure_Y, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId StructureValueObjectType_VectorStructure_Z = new ExpandedNodeId(TestData.Variables.StructureValueObjectType_VectorStructure_Z, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId ArrayValueObjectType_VectorUnionValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_VectorUnionValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_VectorWithOptionalFieldsValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_VectorWithOptionalFieldsValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId ArrayValueObjectType_MultipleVectorsValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_MultipleVectorsValue, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Static_Scalar_VectorUnionValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_VectorUnionValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_VectorWithOptionalFieldsValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_VectorWithOptionalFieldsValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Scalar_MultipleVectorsValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_MultipleVectorsValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_StringValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_ScalarStructure_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_ScalarStructure_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_VectorStructure = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_VectorStructure, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_VectorStructure_X = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_VectorStructure_X, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_VectorStructure_Y = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_VectorStructure_Y, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Structure_VectorStructure_Z = new ExpandedNodeId(TestData.Variables.Data_Static_Structure_VectorStructure_Z, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Static_Array_VectorUnionValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_VectorUnionValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_VectorWithOptionalFieldsValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_VectorWithOptionalFieldsValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Static_Array_MultipleVectorsValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_MultipleVectorsValue, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_VectorUnionValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_VectorUnionValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_VectorWithOptionalFieldsValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_VectorWithOptionalFieldsValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_MultipleVectorsValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_MultipleVectorsValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_SimulationActive, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_BooleanValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_SByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_ByteValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_Int16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_UInt16Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_Int32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_UInt32Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_Int64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_UInt64Value, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_FloatValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_DoubleValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_StringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_StringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_DateTimeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_GuidValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_ByteStringValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_XmlElementValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_NodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_StatusCodeValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_VariantValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_EnumerationValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_StructureValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_NumberValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_IntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_ScalarStructure_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_ScalarStructure_UIntegerValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_VectorStructure = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_VectorStructure, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_VectorStructure_X = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_VectorStructure_X, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_VectorStructure_Y = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_VectorStructure_Y, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Structure_VectorStructure_Z = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Structure_VectorStructure_Z, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId Data_Dynamic_Array_VectorUnionValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_VectorUnionValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_VectorWithOptionalFieldsValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_VectorWithOptionalFieldsValue, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId Data_Dynamic_Array_MultipleVectorsValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_MultipleVectorsValue, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId TestData_BinarySchema_ScalarStructureDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_ScalarStructureDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_ArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_ArrayValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_UserScalarValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_UserScalarValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_UserArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_UserArrayValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_Vector = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_Vector, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_VectorUnion = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_VectorUnion, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_VectorWithOptionalFields = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_VectorWithOptionalFields, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_BinarySchema_MultipleVectors = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_MultipleVectors, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId TestData_XmlSchema_ScalarStructureDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_ScalarStructureDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_ArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_ArrayValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_UserScalarValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_UserScalarValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_UserArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_UserArrayValueDataType, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_Vector = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_Vector, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_VectorUnion = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_VectorUnion, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_VectorWithOptionalFields = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_VectorWithOptionalFields, TestData.Namespaces.TestData);

        /// <remarks />
        public static readonly ExpandedNodeId TestData_XmlSchema_MultipleVectors = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_MultipleVectors, TestData.Namespaces.TestData);

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
        public static readonly ExpandedNodeId ScalarStructureVariableType = new ExpandedNodeId(TestData.VariableTypes.ScalarStructureVariableType, TestData.Namespaces.TestData);

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
        public const string MultipleVectors = "MultipleVectors";

        /// <remarks />
        public const string MultipleVectorsValue = "MultipleVectorsValue";

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
        public const string ScalarStructure = "ScalarStructure";

        /// <remarks />
        public const string ScalarStructureDataType = "ScalarStructureDataType";

        /// <remarks />
        public const string ScalarStructureVariableType = "ScalarStructureVariableType";

        /// <remarks />
        public const string ScalarValueObjectType = "ScalarValueObjectType";

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
        public const string StructureValueObjectType = "StructureValueObjectType";

        /// <remarks />
        public const string TestData_BinarySchema = "TestData";

        /// <remarks />
        public const string TestData_XmlSchema = "TestData";

        /// <remarks />
        public const string TestDataObjectType = "TestDataObjectType";

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
        public const string VectorStructure = "VectorStructure";

        /// <remarks />
        public const string VectorUnion = "VectorUnion";

        /// <remarks />
        public const string VectorUnionValue = "VectorUnionValue";

        /// <remarks />
        public const string VectorValue = "VectorValue";

        /// <remarks />
        public const string VectorVariableType = "VectorVariableType";

        /// <remarks />
        public const string VectorWithOptionalFields = "VectorWithOptionalFields";

        /// <remarks />
        public const string VectorWithOptionalFieldsValue = "VectorWithOptionalFieldsValue";

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