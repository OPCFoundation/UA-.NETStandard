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

namespace TestData
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
        /// The identifier for the ScalarValueDataType DataType.
        /// </summary>
        public const uint ScalarValueDataType = 9440;

        /// <summary>
        /// The identifier for the ArrayValueDataType DataType.
        /// </summary>
        public const uint ArrayValueDataType = 9669;

        /// <summary>
        /// The identifier for the BooleanDataType DataType.
        /// </summary>
        public const uint BooleanDataType = 9898;

        /// <summary>
        /// The identifier for the SByteDataType DataType.
        /// </summary>
        public const uint SByteDataType = 9899;

        /// <summary>
        /// The identifier for the ByteDataType DataType.
        /// </summary>
        public const uint ByteDataType = 9900;

        /// <summary>
        /// The identifier for the Int16DataType DataType.
        /// </summary>
        public const uint Int16DataType = 9901;

        /// <summary>
        /// The identifier for the UInt16DataType DataType.
        /// </summary>
        public const uint UInt16DataType = 9902;

        /// <summary>
        /// The identifier for the Int32DataType DataType.
        /// </summary>
        public const uint Int32DataType = 9903;

        /// <summary>
        /// The identifier for the UInt32DataType DataType.
        /// </summary>
        public const uint UInt32DataType = 9904;

        /// <summary>
        /// The identifier for the Int64DataType DataType.
        /// </summary>
        public const uint Int64DataType = 9905;

        /// <summary>
        /// The identifier for the UInt64DataType DataType.
        /// </summary>
        public const uint UInt64DataType = 9906;

        /// <summary>
        /// The identifier for the FloatDataType DataType.
        /// </summary>
        public const uint FloatDataType = 9907;

        /// <summary>
        /// The identifier for the DoubleDataType DataType.
        /// </summary>
        public const uint DoubleDataType = 9908;

        /// <summary>
        /// The identifier for the StringDataType DataType.
        /// </summary>
        public const uint StringDataType = 9909;

        /// <summary>
        /// The identifier for the DateTimeDataType DataType.
        /// </summary>
        public const uint DateTimeDataType = 9910;

        /// <summary>
        /// The identifier for the GuidDataType DataType.
        /// </summary>
        public const uint GuidDataType = 9911;

        /// <summary>
        /// The identifier for the ByteStringDataType DataType.
        /// </summary>
        public const uint ByteStringDataType = 9912;

        /// <summary>
        /// The identifier for the XmlElementDataType DataType.
        /// </summary>
        public const uint XmlElementDataType = 9913;

        /// <summary>
        /// The identifier for the NodeIdDataType DataType.
        /// </summary>
        public const uint NodeIdDataType = 9914;

        /// <summary>
        /// The identifier for the ExpandedNodeIdDataType DataType.
        /// </summary>
        public const uint ExpandedNodeIdDataType = 9915;

        /// <summary>
        /// The identifier for the QualifiedNameDataType DataType.
        /// </summary>
        public const uint QualifiedNameDataType = 9916;

        /// <summary>
        /// The identifier for the LocalizedTextDataType DataType.
        /// </summary>
        public const uint LocalizedTextDataType = 9917;

        /// <summary>
        /// The identifier for the StatusCodeDataType DataType.
        /// </summary>
        public const uint StatusCodeDataType = 9918;

        /// <summary>
        /// The identifier for the VariantDataType DataType.
        /// </summary>
        public const uint VariantDataType = 9919;

        /// <summary>
        /// The identifier for the UserScalarValueDataType DataType.
        /// </summary>
        public const uint UserScalarValueDataType = 9920;

        /// <summary>
        /// The identifier for the UserArrayValueDataType DataType.
        /// </summary>
        public const uint UserArrayValueDataType = 10006;
    }
    #endregion

    #region Method Identifiers
    /// <summary>
    /// A class that declares constants for all Methods in the Model Design.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public static partial class Methods
    {
        /// <summary>
        /// The identifier for the GenerateValuesMethodType Method.
        /// </summary>
        public const uint GenerateValuesMethodType = 9369;

        /// <summary>
        /// The identifier for the TestDataObjectType_GenerateValues Method.
        /// </summary>
        public const uint TestDataObjectType_GenerateValues = 9385;

        /// <summary>
        /// The identifier for the TestDataObjectType_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint TestDataObjectType_CycleComplete_Acknowledge = 9436;

        /// <summary>
        /// The identifier for the ScalarValue1MethodType Method.
        /// </summary>
        public const uint ScalarValue1MethodType = 9441;

        /// <summary>
        /// The identifier for the ScalarValue2MethodType Method.
        /// </summary>
        public const uint ScalarValue2MethodType = 9444;

        /// <summary>
        /// The identifier for the ScalarValue3MethodType Method.
        /// </summary>
        public const uint ScalarValue3MethodType = 9447;

        /// <summary>
        /// The identifier for the ArrayValue1MethodType Method.
        /// </summary>
        public const uint ArrayValue1MethodType = 9670;

        /// <summary>
        /// The identifier for the ArrayValue2MethodType Method.
        /// </summary>
        public const uint ArrayValue2MethodType = 9673;

        /// <summary>
        /// The identifier for the ArrayValue3MethodType Method.
        /// </summary>
        public const uint ArrayValue3MethodType = 9676;

        /// <summary>
        /// The identifier for the UserScalarValue1MethodType Method.
        /// </summary>
        public const uint UserScalarValue1MethodType = 10000;

        /// <summary>
        /// The identifier for the UserScalarValue2MethodType Method.
        /// </summary>
        public const uint UserScalarValue2MethodType = 10003;

        /// <summary>
        /// The identifier for the UserArrayValue1MethodType Method.
        /// </summary>
        public const uint UserArrayValue1MethodType = 10086;

        /// <summary>
        /// The identifier for the UserArrayValue2MethodType Method.
        /// </summary>
        public const uint UserArrayValue2MethodType = 10089;

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod1 Method.
        /// </summary>
        public const uint MethodTestType_ScalarMethod1 = 10093;

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod2 Method.
        /// </summary>
        public const uint MethodTestType_ScalarMethod2 = 10096;

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod3 Method.
        /// </summary>
        public const uint MethodTestType_ScalarMethod3 = 10099;

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod1 Method.
        /// </summary>
        public const uint MethodTestType_ArrayMethod1 = 10102;

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod2 Method.
        /// </summary>
        public const uint MethodTestType_ArrayMethod2 = 10105;

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod3 Method.
        /// </summary>
        public const uint MethodTestType_ArrayMethod3 = 10108;

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod1 Method.
        /// </summary>
        public const uint MethodTestType_UserScalarMethod1 = 10111;

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod2 Method.
        /// </summary>
        public const uint MethodTestType_UserScalarMethod2 = 10114;

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod1 Method.
        /// </summary>
        public const uint MethodTestType_UserArrayMethod1 = 10117;

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod2 Method.
        /// </summary>
        public const uint MethodTestType_UserArrayMethod2 = 10120;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_GenerateValues Method.
        /// </summary>
        public const uint Data_Static_Scalar_GenerateValues = 10161;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Disable = 10191;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Enable = 10190;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_AddComment = 10192;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Acknowledge = 10212;

        /// <summary>
        /// The identifier for the Data_Static_Array_GenerateValues Method.
        /// </summary>
        public const uint Data_Static_Array_GenerateValues = 10245;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Disable = 10275;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Enable = 10274;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_AddComment = 10276;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Acknowledge = 10296;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_GenerateValues Method.
        /// </summary>
        public const uint Data_Static_UserScalar_GenerateValues = 10329;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Disable = 10359;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Enable = 10358;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_AddComment = 10360;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Acknowledge = 10380;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_GenerateValues Method.
        /// </summary>
        public const uint Data_Static_UserArray_GenerateValues = 10408;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Disable = 10438;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Enable = 10437;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_AddComment = 10439;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Acknowledge = 10459;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_GenerateValues Method.
        /// </summary>
        public const uint Data_Static_AnalogScalar_GenerateValues = 10487;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Disable = 10517;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Enable = 10516;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_AddComment = 10518;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Acknowledge = 10538;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_GenerateValues Method.
        /// </summary>
        public const uint Data_Static_AnalogArray_GenerateValues = 10622;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Disable = 10652;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Enable = 10651;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_AddComment = 10653;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Acknowledge = 10673;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod1 Method.
        /// </summary>
        public const uint Data_Static_MethodTest_ScalarMethod1 = 10756;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod2 Method.
        /// </summary>
        public const uint Data_Static_MethodTest_ScalarMethod2 = 10759;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod3 Method.
        /// </summary>
        public const uint Data_Static_MethodTest_ScalarMethod3 = 10762;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod1 Method.
        /// </summary>
        public const uint Data_Static_MethodTest_ArrayMethod1 = 10765;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod2 Method.
        /// </summary>
        public const uint Data_Static_MethodTest_ArrayMethod2 = 10768;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod3 Method.
        /// </summary>
        public const uint Data_Static_MethodTest_ArrayMethod3 = 10771;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod1 Method.
        /// </summary>
        public const uint Data_Static_MethodTest_UserScalarMethod1 = 10774;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod2 Method.
        /// </summary>
        public const uint Data_Static_MethodTest_UserScalarMethod2 = 10777;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod1 Method.
        /// </summary>
        public const uint Data_Static_MethodTest_UserArrayMethod1 = 10780;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod2 Method.
        /// </summary>
        public const uint Data_Static_MethodTest_UserArrayMethod2 = 10783;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_GenerateValues Method.
        /// </summary>
        public const uint Data_Dynamic_Scalar_GenerateValues = 10789;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Disable = 10819;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Enable = 10818;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_AddComment = 10820;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Acknowledge = 10840;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_GenerateValues Method.
        /// </summary>
        public const uint Data_Dynamic_Array_GenerateValues = 10873;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Disable = 10903;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Enable = 10902;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_AddComment = 10904;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Acknowledge = 10924;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_GenerateValues Method.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_GenerateValues = 10957;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Disable = 10987;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Enable = 10986;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_AddComment = 10988;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Acknowledge = 11008;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_GenerateValues Method.
        /// </summary>
        public const uint Data_Dynamic_UserArray_GenerateValues = 11036;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Disable = 11066;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Enable = 11065;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_AddComment = 11067;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Acknowledge = 11087;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_GenerateValues Method.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_GenerateValues = 11115;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Disable = 11145;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Enable = 11144;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AddComment = 11146;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge = 11166;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_GenerateValues Method.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_GenerateValues = 11250;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Disable Method.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Disable = 11280;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Enable Method.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Enable = 11279;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_AddComment Method.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AddComment = 11281;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Acknowledge Method.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Acknowledge = 11301;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Disable Method.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_Disable = 11412;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Enable Method.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_Enable = 11411;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_AddComment Method.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_AddComment = 11413;
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
        /// The identifier for the TestDataObjectType_CycleComplete Object.
        /// </summary>
        public const uint TestDataObjectType_CycleComplete = 9387;

        /// <summary>
        /// The identifier for the Data Object.
        /// </summary>
        public const uint Data = 10157;

        /// <summary>
        /// The identifier for the Data_Static Object.
        /// </summary>
        public const uint Data_Static = 10158;

        /// <summary>
        /// The identifier for the Data_Static_Scalar Object.
        /// </summary>
        public const uint Data_Static_Scalar = 10159;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete Object.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete = 10163;

        /// <summary>
        /// The identifier for the Data_Static_Array Object.
        /// </summary>
        public const uint Data_Static_Array = 10243;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete Object.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete = 10247;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar Object.
        /// </summary>
        public const uint Data_Static_UserScalar = 10327;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete Object.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete = 10331;

        /// <summary>
        /// The identifier for the Data_Static_UserArray Object.
        /// </summary>
        public const uint Data_Static_UserArray = 10406;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete Object.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete = 10410;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar Object.
        /// </summary>
        public const uint Data_Static_AnalogScalar = 10485;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete Object.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete = 10489;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray Object.
        /// </summary>
        public const uint Data_Static_AnalogArray = 10620;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete Object.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete = 10624;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest Object.
        /// </summary>
        public const uint Data_Static_MethodTest = 10755;

        /// <summary>
        /// The identifier for the Data_Dynamic Object.
        /// </summary>
        public const uint Data_Dynamic = 10786;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar Object.
        /// </summary>
        public const uint Data_Dynamic_Scalar = 10787;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete Object.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete = 10791;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array Object.
        /// </summary>
        public const uint Data_Dynamic_Array = 10871;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete Object.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete = 10875;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar Object.
        /// </summary>
        public const uint Data_Dynamic_UserScalar = 10955;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete Object.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete = 10959;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray Object.
        /// </summary>
        public const uint Data_Dynamic_UserArray = 11034;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete Object.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete = 11038;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar Object.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar = 11113;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete Object.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete = 11117;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray Object.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray = 11248;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete Object.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete = 11252;

        /// <summary>
        /// The identifier for the Data_Conditions Object.
        /// </summary>
        public const uint Data_Conditions = 11383;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus Object.
        /// </summary>
        public const uint Data_Conditions_SystemStatus = 11384;

        /// <summary>
        /// The identifier for the ScalarValueDataType_Encoding_DefaultXml Object.
        /// </summary>
        public const uint ScalarValueDataType_Encoding_DefaultXml = 11418;

        /// <summary>
        /// The identifier for the ArrayValueDataType_Encoding_DefaultXml Object.
        /// </summary>
        public const uint ArrayValueDataType_Encoding_DefaultXml = 11419;

        /// <summary>
        /// The identifier for the UserScalarValueDataType_Encoding_DefaultXml Object.
        /// </summary>
        public const uint UserScalarValueDataType_Encoding_DefaultXml = 11420;

        /// <summary>
        /// The identifier for the UserArrayValueDataType_Encoding_DefaultXml Object.
        /// </summary>
        public const uint UserArrayValueDataType_Encoding_DefaultXml = 11421;

        /// <summary>
        /// The identifier for the ScalarValueDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public const uint ScalarValueDataType_Encoding_DefaultBinary = 11437;

        /// <summary>
        /// The identifier for the ArrayValueDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public const uint ArrayValueDataType_Encoding_DefaultBinary = 11438;

        /// <summary>
        /// The identifier for the UserScalarValueDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public const uint UserScalarValueDataType_Encoding_DefaultBinary = 11439;

        /// <summary>
        /// The identifier for the UserArrayValueDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public const uint UserArrayValueDataType_Encoding_DefaultBinary = 11440;
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
        /// The identifier for the GenerateValuesEventType ObjectType.
        /// </summary>
        public const uint GenerateValuesEventType = 9371;

        /// <summary>
        /// The identifier for the TestDataObjectType ObjectType.
        /// </summary>
        public const uint TestDataObjectType = 9383;

        /// <summary>
        /// The identifier for the ScalarValueObjectType ObjectType.
        /// </summary>
        public const uint ScalarValueObjectType = 9450;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType ObjectType.
        /// </summary>
        public const uint AnalogScalarValueObjectType = 9534;

        /// <summary>
        /// The identifier for the ArrayValueObjectType ObjectType.
        /// </summary>
        public const uint ArrayValueObjectType = 9679;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType ObjectType.
        /// </summary>
        public const uint AnalogArrayValueObjectType = 9763;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType ObjectType.
        /// </summary>
        public const uint UserScalarValueObjectType = 9921;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType ObjectType.
        /// </summary>
        public const uint UserArrayValueObjectType = 10007;

        /// <summary>
        /// The identifier for the MethodTestType ObjectType.
        /// </summary>
        public const uint MethodTestType = 10092;

        /// <summary>
        /// The identifier for the TestSystemConditionType ObjectType.
        /// </summary>
        public const uint TestSystemConditionType = 10123;
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
        /// The identifier for the GenerateValuesMethodType_InputArguments Variable.
        /// </summary>
        public const uint GenerateValuesMethodType_InputArguments = 9370;

        /// <summary>
        /// The identifier for the GenerateValuesEventType_Iterations Variable.
        /// </summary>
        public const uint GenerateValuesEventType_Iterations = 9381;

        /// <summary>
        /// The identifier for the GenerateValuesEventType_NewValueCount Variable.
        /// </summary>
        public const uint GenerateValuesEventType_NewValueCount = 9382;

        /// <summary>
        /// The identifier for the TestDataObjectType_SimulationActive Variable.
        /// </summary>
        public const uint TestDataObjectType_SimulationActive = 9384;

        /// <summary>
        /// The identifier for the TestDataObjectType_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint TestDataObjectType_GenerateValues_InputArguments = 9386;

        /// <summary>
        /// The identifier for the TestDataObjectType_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint TestDataObjectType_CycleComplete_AckedState = 9420;

        /// <summary>
        /// The identifier for the TestDataObjectType_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint TestDataObjectType_CycleComplete_Acknowledge_InputArguments = 9437;

        /// <summary>
        /// The identifier for the TestDataObjectType_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint TestDataObjectType_CycleComplete_Confirm_InputArguments = 9439;

        /// <summary>
        /// The identifier for the ScalarValue1MethodType_InputArguments Variable.
        /// </summary>
        public const uint ScalarValue1MethodType_InputArguments = 9442;

        /// <summary>
        /// The identifier for the ScalarValue1MethodType_OutputArguments Variable.
        /// </summary>
        public const uint ScalarValue1MethodType_OutputArguments = 9443;

        /// <summary>
        /// The identifier for the ScalarValue2MethodType_InputArguments Variable.
        /// </summary>
        public const uint ScalarValue2MethodType_InputArguments = 9445;

        /// <summary>
        /// The identifier for the ScalarValue2MethodType_OutputArguments Variable.
        /// </summary>
        public const uint ScalarValue2MethodType_OutputArguments = 9446;

        /// <summary>
        /// The identifier for the ScalarValue3MethodType_InputArguments Variable.
        /// </summary>
        public const uint ScalarValue3MethodType_InputArguments = 9448;

        /// <summary>
        /// The identifier for the ScalarValue3MethodType_OutputArguments Variable.
        /// </summary>
        public const uint ScalarValue3MethodType_OutputArguments = 9449;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_BooleanValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_BooleanValue = 9507;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_SByteValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_SByteValue = 9508;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_ByteValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_ByteValue = 9509;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_Int16Value Variable.
        /// </summary>
        public const uint ScalarValueObjectType_Int16Value = 9510;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_UInt16Value Variable.
        /// </summary>
        public const uint ScalarValueObjectType_UInt16Value = 9511;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_Int32Value Variable.
        /// </summary>
        public const uint ScalarValueObjectType_Int32Value = 9512;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_UInt32Value Variable.
        /// </summary>
        public const uint ScalarValueObjectType_UInt32Value = 9513;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_Int64Value Variable.
        /// </summary>
        public const uint ScalarValueObjectType_Int64Value = 9514;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_UInt64Value Variable.
        /// </summary>
        public const uint ScalarValueObjectType_UInt64Value = 9515;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_FloatValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_FloatValue = 9516;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_DoubleValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_DoubleValue = 9517;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_StringValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_StringValue = 9518;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_DateTimeValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_DateTimeValue = 9519;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_GuidValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_GuidValue = 9520;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_ByteStringValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_ByteStringValue = 9521;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_XmlElementValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_XmlElementValue = 9522;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_NodeIdValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_NodeIdValue = 9523;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_ExpandedNodeIdValue = 9524;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_QualifiedNameValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_QualifiedNameValue = 9525;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_LocalizedTextValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_LocalizedTextValue = 9526;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_StatusCodeValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_StatusCodeValue = 9527;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_VariantValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_VariantValue = 9528;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_EnumerationValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_EnumerationValue = 9529;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_StructureValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_StructureValue = 9530;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_NumberValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_NumberValue = 9531;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_IntegerValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_IntegerValue = 9532;

        /// <summary>
        /// The identifier for the ScalarValueObjectType_UIntegerValue Variable.
        /// </summary>
        public const uint ScalarValueObjectType_UIntegerValue = 9533;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_SByteValue Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_SByteValue = 9591;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_SByteValue_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_SByteValue_EURange = 9594;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_ByteValue Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_ByteValue = 9597;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_ByteValue_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_ByteValue_EURange = 9600;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int16Value Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_Int16Value = 9603;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int16Value_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_Int16Value_EURange = 9606;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt16Value Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_UInt16Value = 9609;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt16Value_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_UInt16Value_EURange = 9612;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int32Value Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_Int32Value = 9615;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int32Value_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_Int32Value_EURange = 9618;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt32Value Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_UInt32Value = 9621;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt32Value_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_UInt32Value_EURange = 9624;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int64Value Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_Int64Value = 9627;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int64Value_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_Int64Value_EURange = 9630;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt64Value Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_UInt64Value = 9633;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt64Value_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_UInt64Value_EURange = 9636;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_FloatValue Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_FloatValue = 9639;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_FloatValue_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_FloatValue_EURange = 9642;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_DoubleValue Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_DoubleValue = 9645;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_DoubleValue_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_DoubleValue_EURange = 9648;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_NumberValue Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_NumberValue = 9651;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_NumberValue_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_NumberValue_EURange = 9654;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_IntegerValue Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_IntegerValue = 9657;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_IntegerValue_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_IntegerValue_EURange = 9660;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UIntegerValue Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_UIntegerValue = 9663;

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UIntegerValue_EURange Variable.
        /// </summary>
        public const uint AnalogScalarValueObjectType_UIntegerValue_EURange = 9666;

        /// <summary>
        /// The identifier for the ArrayValue1MethodType_InputArguments Variable.
        /// </summary>
        public const uint ArrayValue1MethodType_InputArguments = 9671;

        /// <summary>
        /// The identifier for the ArrayValue1MethodType_OutputArguments Variable.
        /// </summary>
        public const uint ArrayValue1MethodType_OutputArguments = 9672;

        /// <summary>
        /// The identifier for the ArrayValue2MethodType_InputArguments Variable.
        /// </summary>
        public const uint ArrayValue2MethodType_InputArguments = 9674;

        /// <summary>
        /// The identifier for the ArrayValue2MethodType_OutputArguments Variable.
        /// </summary>
        public const uint ArrayValue2MethodType_OutputArguments = 9675;

        /// <summary>
        /// The identifier for the ArrayValue3MethodType_InputArguments Variable.
        /// </summary>
        public const uint ArrayValue3MethodType_InputArguments = 9677;

        /// <summary>
        /// The identifier for the ArrayValue3MethodType_OutputArguments Variable.
        /// </summary>
        public const uint ArrayValue3MethodType_OutputArguments = 9678;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_BooleanValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_BooleanValue = 9736;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_SByteValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_SByteValue = 9737;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_ByteValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_ByteValue = 9738;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_Int16Value Variable.
        /// </summary>
        public const uint ArrayValueObjectType_Int16Value = 9739;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_UInt16Value Variable.
        /// </summary>
        public const uint ArrayValueObjectType_UInt16Value = 9740;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_Int32Value Variable.
        /// </summary>
        public const uint ArrayValueObjectType_Int32Value = 9741;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_UInt32Value Variable.
        /// </summary>
        public const uint ArrayValueObjectType_UInt32Value = 9742;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_Int64Value Variable.
        /// </summary>
        public const uint ArrayValueObjectType_Int64Value = 9743;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_UInt64Value Variable.
        /// </summary>
        public const uint ArrayValueObjectType_UInt64Value = 9744;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_FloatValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_FloatValue = 9745;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_DoubleValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_DoubleValue = 9746;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_StringValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_StringValue = 9747;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_DateTimeValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_DateTimeValue = 9748;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_GuidValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_GuidValue = 9749;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_ByteStringValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_ByteStringValue = 9750;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_XmlElementValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_XmlElementValue = 9751;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_NodeIdValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_NodeIdValue = 9752;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_ExpandedNodeIdValue = 9753;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_QualifiedNameValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_QualifiedNameValue = 9754;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_LocalizedTextValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_LocalizedTextValue = 9755;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_StatusCodeValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_StatusCodeValue = 9756;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_VariantValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_VariantValue = 9757;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_EnumerationValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_EnumerationValue = 9758;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_StructureValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_StructureValue = 9759;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_NumberValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_NumberValue = 9760;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_IntegerValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_IntegerValue = 9761;

        /// <summary>
        /// The identifier for the ArrayValueObjectType_UIntegerValue Variable.
        /// </summary>
        public const uint ArrayValueObjectType_UIntegerValue = 9762;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_SByteValue Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_SByteValue = 9820;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_SByteValue_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_SByteValue_EURange = 9823;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_ByteValue Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_ByteValue = 9826;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_ByteValue_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_ByteValue_EURange = 9829;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int16Value Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_Int16Value = 9832;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int16Value_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_Int16Value_EURange = 9835;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt16Value Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_UInt16Value = 9838;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt16Value_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_UInt16Value_EURange = 9841;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int32Value Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_Int32Value = 9844;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int32Value_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_Int32Value_EURange = 9847;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt32Value Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_UInt32Value = 9850;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt32Value_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_UInt32Value_EURange = 9853;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int64Value Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_Int64Value = 9856;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int64Value_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_Int64Value_EURange = 9859;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt64Value Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_UInt64Value = 9862;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt64Value_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_UInt64Value_EURange = 9865;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_FloatValue Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_FloatValue = 9868;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_FloatValue_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_FloatValue_EURange = 9871;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_DoubleValue Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_DoubleValue = 9874;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_DoubleValue_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_DoubleValue_EURange = 9877;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_NumberValue Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_NumberValue = 9880;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_NumberValue_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_NumberValue_EURange = 9883;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_IntegerValue Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_IntegerValue = 9886;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_IntegerValue_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_IntegerValue_EURange = 9889;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UIntegerValue Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_UIntegerValue = 9892;

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UIntegerValue_EURange Variable.
        /// </summary>
        public const uint AnalogArrayValueObjectType_UIntegerValue_EURange = 9895;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_BooleanValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_BooleanValue = 9978;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_SByteValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_SByteValue = 9979;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_ByteValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_ByteValue = 9980;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_Int16Value Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_Int16Value = 9981;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_UInt16Value Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_UInt16Value = 9982;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_Int32Value Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_Int32Value = 9983;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_UInt32Value Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_UInt32Value = 9984;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_Int64Value Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_Int64Value = 9985;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_UInt64Value Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_UInt64Value = 9986;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_FloatValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_FloatValue = 9987;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_DoubleValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_DoubleValue = 9988;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_StringValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_StringValue = 9989;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_DateTimeValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_DateTimeValue = 9990;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_GuidValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_GuidValue = 9991;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_ByteStringValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_ByteStringValue = 9992;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_XmlElementValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_XmlElementValue = 9993;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_NodeIdValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_NodeIdValue = 9994;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_ExpandedNodeIdValue = 9995;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_QualifiedNameValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_QualifiedNameValue = 9996;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_LocalizedTextValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_LocalizedTextValue = 9997;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_StatusCodeValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_StatusCodeValue = 9998;

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_VariantValue Variable.
        /// </summary>
        public const uint UserScalarValueObjectType_VariantValue = 9999;

        /// <summary>
        /// The identifier for the UserScalarValue1MethodType_InputArguments Variable.
        /// </summary>
        public const uint UserScalarValue1MethodType_InputArguments = 10001;

        /// <summary>
        /// The identifier for the UserScalarValue1MethodType_OutputArguments Variable.
        /// </summary>
        public const uint UserScalarValue1MethodType_OutputArguments = 10002;

        /// <summary>
        /// The identifier for the UserScalarValue2MethodType_InputArguments Variable.
        /// </summary>
        public const uint UserScalarValue2MethodType_InputArguments = 10004;

        /// <summary>
        /// The identifier for the UserScalarValue2MethodType_OutputArguments Variable.
        /// </summary>
        public const uint UserScalarValue2MethodType_OutputArguments = 10005;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_BooleanValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_BooleanValue = 10064;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_SByteValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_SByteValue = 10065;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_ByteValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_ByteValue = 10066;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_Int16Value Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_Int16Value = 10067;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_UInt16Value Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_UInt16Value = 10068;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_Int32Value Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_Int32Value = 10069;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_UInt32Value Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_UInt32Value = 10070;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_Int64Value Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_Int64Value = 10071;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_UInt64Value Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_UInt64Value = 10072;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_FloatValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_FloatValue = 10073;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_DoubleValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_DoubleValue = 10074;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_StringValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_StringValue = 10075;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_DateTimeValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_DateTimeValue = 10076;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_GuidValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_GuidValue = 10077;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_ByteStringValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_ByteStringValue = 10078;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_XmlElementValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_XmlElementValue = 10079;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_NodeIdValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_NodeIdValue = 10080;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_ExpandedNodeIdValue = 10081;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_QualifiedNameValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_QualifiedNameValue = 10082;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_LocalizedTextValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_LocalizedTextValue = 10083;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_StatusCodeValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_StatusCodeValue = 10084;

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_VariantValue Variable.
        /// </summary>
        public const uint UserArrayValueObjectType_VariantValue = 10085;

        /// <summary>
        /// The identifier for the UserArrayValue1MethodType_InputArguments Variable.
        /// </summary>
        public const uint UserArrayValue1MethodType_InputArguments = 10087;

        /// <summary>
        /// The identifier for the UserArrayValue1MethodType_OutputArguments Variable.
        /// </summary>
        public const uint UserArrayValue1MethodType_OutputArguments = 10088;

        /// <summary>
        /// The identifier for the UserArrayValue2MethodType_InputArguments Variable.
        /// </summary>
        public const uint UserArrayValue2MethodType_InputArguments = 10090;

        /// <summary>
        /// The identifier for the UserArrayValue2MethodType_OutputArguments Variable.
        /// </summary>
        public const uint UserArrayValue2MethodType_OutputArguments = 10091;

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod1_InputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ScalarMethod1_InputArguments = 10094;

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod1_OutputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ScalarMethod1_OutputArguments = 10095;

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod2_InputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ScalarMethod2_InputArguments = 10097;

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod2_OutputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ScalarMethod2_OutputArguments = 10098;

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod3_InputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ScalarMethod3_InputArguments = 10100;

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod3_OutputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ScalarMethod3_OutputArguments = 10101;

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod1_InputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ArrayMethod1_InputArguments = 10103;

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod1_OutputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ArrayMethod1_OutputArguments = 10104;

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod2_InputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ArrayMethod2_InputArguments = 10106;

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod2_OutputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ArrayMethod2_OutputArguments = 10107;

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod3_InputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ArrayMethod3_InputArguments = 10109;

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod3_OutputArguments Variable.
        /// </summary>
        public const uint MethodTestType_ArrayMethod3_OutputArguments = 10110;

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod1_InputArguments Variable.
        /// </summary>
        public const uint MethodTestType_UserScalarMethod1_InputArguments = 10112;

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod1_OutputArguments Variable.
        /// </summary>
        public const uint MethodTestType_UserScalarMethod1_OutputArguments = 10113;

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod2_InputArguments Variable.
        /// </summary>
        public const uint MethodTestType_UserScalarMethod2_InputArguments = 10115;

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod2_OutputArguments Variable.
        /// </summary>
        public const uint MethodTestType_UserScalarMethod2_OutputArguments = 10116;

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod1_InputArguments Variable.
        /// </summary>
        public const uint MethodTestType_UserArrayMethod1_InputArguments = 10118;

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod1_OutputArguments Variable.
        /// </summary>
        public const uint MethodTestType_UserArrayMethod1_OutputArguments = 10119;

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod2_InputArguments Variable.
        /// </summary>
        public const uint MethodTestType_UserArrayMethod2_InputArguments = 10121;

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod2_OutputArguments Variable.
        /// </summary>
        public const uint MethodTestType_UserArrayMethod2_OutputArguments = 10122;

        /// <summary>
        /// The identifier for the TestSystemConditionType_MonitoredNodeCount Variable.
        /// </summary>
        public const uint TestSystemConditionType_MonitoredNodeCount = 10156;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_SimulationActive Variable.
        /// </summary>
        public const uint Data_Static_Scalar_SimulationActive = 10160;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_Scalar_GenerateValues_InputArguments = 10162;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_EventId = 10164;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_EventType = 10165;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_SourceNode = 10166;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_SourceName = 10167;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Time = 10168;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_ReceiveTime = 10169;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_LocalTime = 10170;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Message = 10171;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Severity = 10172;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_ConditionClassId = 11594;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_ConditionClassName = 11595;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_ConditionName = 11565;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_BranchId = 10173;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Retain = 10174;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_EnabledState = 10175;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_EnabledState_Id = 10176;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Quality = 10181;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Quality_SourceTimestamp = 10182;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_LastSeverity = 10185;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_LastSeverity_SourceTimestamp = 10186;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Comment = 10187;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Comment_SourceTimestamp = 10188;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_ClientUserId = 10189;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_AddComment_InputArguments = 10193;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_AckedState = 10196;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_AckedState_Id = 10197;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_ConfirmedState_Id = 10205;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Acknowledge_InputArguments = 10213;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_Scalar_CycleComplete_Confirm_InputArguments = 10215;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_BooleanValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_BooleanValue = 10216;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_SByteValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_SByteValue = 10217;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_ByteValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_ByteValue = 10218;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_Int16Value Variable.
        /// </summary>
        public const uint Data_Static_Scalar_Int16Value = 10219;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_UInt16Value Variable.
        /// </summary>
        public const uint Data_Static_Scalar_UInt16Value = 10220;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_Int32Value Variable.
        /// </summary>
        public const uint Data_Static_Scalar_Int32Value = 10221;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_UInt32Value Variable.
        /// </summary>
        public const uint Data_Static_Scalar_UInt32Value = 10222;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_Int64Value Variable.
        /// </summary>
        public const uint Data_Static_Scalar_Int64Value = 10223;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_UInt64Value Variable.
        /// </summary>
        public const uint Data_Static_Scalar_UInt64Value = 10224;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_FloatValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_FloatValue = 10225;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_DoubleValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_DoubleValue = 10226;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_StringValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_StringValue = 10227;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_DateTimeValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_DateTimeValue = 10228;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_GuidValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_GuidValue = 10229;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_ByteStringValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_ByteStringValue = 10230;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_XmlElementValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_XmlElementValue = 10231;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_NodeIdValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_NodeIdValue = 10232;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_ExpandedNodeIdValue = 10233;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_QualifiedNameValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_QualifiedNameValue = 10234;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_LocalizedTextValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_LocalizedTextValue = 10235;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_StatusCodeValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_StatusCodeValue = 10236;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_VariantValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_VariantValue = 10237;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_EnumerationValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_EnumerationValue = 10238;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_StructureValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_StructureValue = 10239;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_NumberValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_NumberValue = 10240;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_IntegerValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_IntegerValue = 10241;

        /// <summary>
        /// The identifier for the Data_Static_Scalar_UIntegerValue Variable.
        /// </summary>
        public const uint Data_Static_Scalar_UIntegerValue = 10242;

        /// <summary>
        /// The identifier for the Data_Static_Array_SimulationActive Variable.
        /// </summary>
        public const uint Data_Static_Array_SimulationActive = 10244;

        /// <summary>
        /// The identifier for the Data_Static_Array_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_Array_GenerateValues_InputArguments = 10246;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_EventId = 10248;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_EventType = 10249;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_SourceNode = 10250;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_SourceName = 10251;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Time = 10252;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_ReceiveTime = 10253;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_LocalTime = 10254;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Message = 10255;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Severity = 10256;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_ConditionClassId = 11596;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_ConditionClassName = 11597;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_ConditionName = 11566;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_BranchId = 10257;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Retain = 10258;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_EnabledState = 10259;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_EnabledState_Id = 10260;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Quality = 10265;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Quality_SourceTimestamp = 10266;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_LastSeverity = 10269;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_LastSeverity_SourceTimestamp = 10270;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Comment = 10271;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Comment_SourceTimestamp = 10272;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_ClientUserId = 10273;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_AddComment_InputArguments = 10277;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_AckedState = 10280;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_AckedState_Id = 10281;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_ConfirmedState_Id = 10289;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Acknowledge_InputArguments = 10297;

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_Array_CycleComplete_Confirm_InputArguments = 10299;

        /// <summary>
        /// The identifier for the Data_Static_Array_BooleanValue Variable.
        /// </summary>
        public const uint Data_Static_Array_BooleanValue = 10300;

        /// <summary>
        /// The identifier for the Data_Static_Array_SByteValue Variable.
        /// </summary>
        public const uint Data_Static_Array_SByteValue = 10301;

        /// <summary>
        /// The identifier for the Data_Static_Array_ByteValue Variable.
        /// </summary>
        public const uint Data_Static_Array_ByteValue = 10302;

        /// <summary>
        /// The identifier for the Data_Static_Array_Int16Value Variable.
        /// </summary>
        public const uint Data_Static_Array_Int16Value = 10303;

        /// <summary>
        /// The identifier for the Data_Static_Array_UInt16Value Variable.
        /// </summary>
        public const uint Data_Static_Array_UInt16Value = 10304;

        /// <summary>
        /// The identifier for the Data_Static_Array_Int32Value Variable.
        /// </summary>
        public const uint Data_Static_Array_Int32Value = 10305;

        /// <summary>
        /// The identifier for the Data_Static_Array_UInt32Value Variable.
        /// </summary>
        public const uint Data_Static_Array_UInt32Value = 10306;

        /// <summary>
        /// The identifier for the Data_Static_Array_Int64Value Variable.
        /// </summary>
        public const uint Data_Static_Array_Int64Value = 10307;

        /// <summary>
        /// The identifier for the Data_Static_Array_UInt64Value Variable.
        /// </summary>
        public const uint Data_Static_Array_UInt64Value = 10308;

        /// <summary>
        /// The identifier for the Data_Static_Array_FloatValue Variable.
        /// </summary>
        public const uint Data_Static_Array_FloatValue = 10309;

        /// <summary>
        /// The identifier for the Data_Static_Array_DoubleValue Variable.
        /// </summary>
        public const uint Data_Static_Array_DoubleValue = 10310;

        /// <summary>
        /// The identifier for the Data_Static_Array_StringValue Variable.
        /// </summary>
        public const uint Data_Static_Array_StringValue = 10311;

        /// <summary>
        /// The identifier for the Data_Static_Array_DateTimeValue Variable.
        /// </summary>
        public const uint Data_Static_Array_DateTimeValue = 10312;

        /// <summary>
        /// The identifier for the Data_Static_Array_GuidValue Variable.
        /// </summary>
        public const uint Data_Static_Array_GuidValue = 10313;

        /// <summary>
        /// The identifier for the Data_Static_Array_ByteStringValue Variable.
        /// </summary>
        public const uint Data_Static_Array_ByteStringValue = 10314;

        /// <summary>
        /// The identifier for the Data_Static_Array_XmlElementValue Variable.
        /// </summary>
        public const uint Data_Static_Array_XmlElementValue = 10315;

        /// <summary>
        /// The identifier for the Data_Static_Array_NodeIdValue Variable.
        /// </summary>
        public const uint Data_Static_Array_NodeIdValue = 10316;

        /// <summary>
        /// The identifier for the Data_Static_Array_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint Data_Static_Array_ExpandedNodeIdValue = 10317;

        /// <summary>
        /// The identifier for the Data_Static_Array_QualifiedNameValue Variable.
        /// </summary>
        public const uint Data_Static_Array_QualifiedNameValue = 10318;

        /// <summary>
        /// The identifier for the Data_Static_Array_LocalizedTextValue Variable.
        /// </summary>
        public const uint Data_Static_Array_LocalizedTextValue = 10319;

        /// <summary>
        /// The identifier for the Data_Static_Array_StatusCodeValue Variable.
        /// </summary>
        public const uint Data_Static_Array_StatusCodeValue = 10320;

        /// <summary>
        /// The identifier for the Data_Static_Array_VariantValue Variable.
        /// </summary>
        public const uint Data_Static_Array_VariantValue = 10321;

        /// <summary>
        /// The identifier for the Data_Static_Array_EnumerationValue Variable.
        /// </summary>
        public const uint Data_Static_Array_EnumerationValue = 10322;

        /// <summary>
        /// The identifier for the Data_Static_Array_StructureValue Variable.
        /// </summary>
        public const uint Data_Static_Array_StructureValue = 10323;

        /// <summary>
        /// The identifier for the Data_Static_Array_NumberValue Variable.
        /// </summary>
        public const uint Data_Static_Array_NumberValue = 10324;

        /// <summary>
        /// The identifier for the Data_Static_Array_IntegerValue Variable.
        /// </summary>
        public const uint Data_Static_Array_IntegerValue = 10325;

        /// <summary>
        /// The identifier for the Data_Static_Array_UIntegerValue Variable.
        /// </summary>
        public const uint Data_Static_Array_UIntegerValue = 10326;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_SimulationActive Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_SimulationActive = 10328;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_GenerateValues_InputArguments = 10330;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_EventId = 10332;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_EventType = 10333;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_SourceNode = 10334;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_SourceName = 10335;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Time = 10336;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_ReceiveTime = 10337;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_LocalTime = 10338;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Message = 10339;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Severity = 10340;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_ConditionClassId = 11598;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_ConditionClassName = 11599;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_ConditionName = 11567;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_BranchId = 10341;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Retain = 10342;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_EnabledState = 10343;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_EnabledState_Id = 10344;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Quality = 10349;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Quality_SourceTimestamp = 10350;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_LastSeverity = 10353;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = 10354;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Comment = 10355;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Comment_SourceTimestamp = 10356;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_ClientUserId = 10357;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_AddComment_InputArguments = 10361;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_AckedState = 10364;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_AckedState_Id = 10365;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_ConfirmedState_Id = 10373;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Acknowledge_InputArguments = 10381;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_CycleComplete_Confirm_InputArguments = 10383;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_BooleanValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_BooleanValue = 10384;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_SByteValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_SByteValue = 10385;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_ByteValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_ByteValue = 10386;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_Int16Value Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_Int16Value = 10387;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_UInt16Value Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_UInt16Value = 10388;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_Int32Value Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_Int32Value = 10389;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_UInt32Value Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_UInt32Value = 10390;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_Int64Value Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_Int64Value = 10391;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_UInt64Value Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_UInt64Value = 10392;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_FloatValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_FloatValue = 10393;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_DoubleValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_DoubleValue = 10394;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_StringValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_StringValue = 10395;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_DateTimeValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_DateTimeValue = 10396;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_GuidValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_GuidValue = 10397;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_ByteStringValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_ByteStringValue = 10398;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_XmlElementValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_XmlElementValue = 10399;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_NodeIdValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_NodeIdValue = 10400;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_ExpandedNodeIdValue = 10401;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_QualifiedNameValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_QualifiedNameValue = 10402;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_LocalizedTextValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_LocalizedTextValue = 10403;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_StatusCodeValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_StatusCodeValue = 10404;

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_VariantValue Variable.
        /// </summary>
        public const uint Data_Static_UserScalar_VariantValue = 10405;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_SimulationActive Variable.
        /// </summary>
        public const uint Data_Static_UserArray_SimulationActive = 10407;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_UserArray_GenerateValues_InputArguments = 10409;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_EventId = 10411;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_EventType = 10412;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_SourceNode = 10413;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_SourceName = 10414;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Time = 10415;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_ReceiveTime = 10416;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_LocalTime = 10417;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Message = 10418;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Severity = 10419;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_ConditionClassId = 11600;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_ConditionClassName = 11601;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_ConditionName = 11568;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_BranchId = 10420;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Retain = 10421;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_EnabledState = 10422;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_EnabledState_Id = 10423;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Quality = 10428;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Quality_SourceTimestamp = 10429;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_LastSeverity = 10432;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_LastSeverity_SourceTimestamp = 10433;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Comment = 10434;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Comment_SourceTimestamp = 10435;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_ClientUserId = 10436;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_AddComment_InputArguments = 10440;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_AckedState = 10443;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_AckedState_Id = 10444;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_ConfirmedState_Id = 10452;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Acknowledge_InputArguments = 10460;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_UserArray_CycleComplete_Confirm_InputArguments = 10462;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_BooleanValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_BooleanValue = 10463;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_SByteValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_SByteValue = 10464;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_ByteValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_ByteValue = 10465;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_Int16Value Variable.
        /// </summary>
        public const uint Data_Static_UserArray_Int16Value = 10466;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_UInt16Value Variable.
        /// </summary>
        public const uint Data_Static_UserArray_UInt16Value = 10467;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_Int32Value Variable.
        /// </summary>
        public const uint Data_Static_UserArray_Int32Value = 10468;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_UInt32Value Variable.
        /// </summary>
        public const uint Data_Static_UserArray_UInt32Value = 10469;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_Int64Value Variable.
        /// </summary>
        public const uint Data_Static_UserArray_Int64Value = 10470;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_UInt64Value Variable.
        /// </summary>
        public const uint Data_Static_UserArray_UInt64Value = 10471;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_FloatValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_FloatValue = 10472;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_DoubleValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_DoubleValue = 10473;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_StringValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_StringValue = 10474;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_DateTimeValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_DateTimeValue = 10475;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_GuidValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_GuidValue = 10476;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_ByteStringValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_ByteStringValue = 10477;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_XmlElementValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_XmlElementValue = 10478;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_NodeIdValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_NodeIdValue = 10479;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_ExpandedNodeIdValue = 10480;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_QualifiedNameValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_QualifiedNameValue = 10481;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_LocalizedTextValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_LocalizedTextValue = 10482;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_StatusCodeValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_StatusCodeValue = 10483;

        /// <summary>
        /// The identifier for the Data_Static_UserArray_VariantValue Variable.
        /// </summary>
        public const uint Data_Static_UserArray_VariantValue = 10484;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_SimulationActive Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_SimulationActive = 10486;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_GenerateValues_InputArguments = 10488;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_EventId = 10490;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_EventType = 10491;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_SourceNode = 10492;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_SourceName = 10493;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Time = 10494;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_ReceiveTime = 10495;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_LocalTime = 10496;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Message = 10497;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Severity = 10498;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionClassId = 11602;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionClassName = 11603;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_ConditionName = 11569;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_BranchId = 10499;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Retain = 10500;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_EnabledState = 10501;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_EnabledState_Id = 10502;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Quality = 10507;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Quality_SourceTimestamp = 10508;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_LastSeverity = 10511;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = 10512;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Comment = 10513;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Comment_SourceTimestamp = 10514;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_ClientUserId = 10515;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_AddComment_InputArguments = 10519;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_AckedState = 10522;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_AckedState_Id = 10523;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_ConfirmedState_Id = 10531;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Acknowledge_InputArguments = 10539;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_CycleComplete_Confirm_InputArguments = 10541;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_SByteValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_SByteValue = 10542;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_SByteValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_SByteValue_EURange = 10545;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_ByteValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_ByteValue = 10548;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_ByteValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_ByteValue_EURange = 10551;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int16Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_Int16Value = 10554;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int16Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_Int16Value_EURange = 10557;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt16Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_UInt16Value = 10560;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt16Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_UInt16Value_EURange = 10563;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int32Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_Int32Value = 10566;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int32Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_Int32Value_EURange = 10569;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt32Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_UInt32Value = 10572;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt32Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_UInt32Value_EURange = 10575;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int64Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_Int64Value = 10578;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int64Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_Int64Value_EURange = 10581;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt64Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_UInt64Value = 10584;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt64Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_UInt64Value_EURange = 10587;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_FloatValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_FloatValue = 10590;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_FloatValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_FloatValue_EURange = 10593;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_DoubleValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_DoubleValue = 10596;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_DoubleValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_DoubleValue_EURange = 10599;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_NumberValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_NumberValue = 10602;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_NumberValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_NumberValue_EURange = 10605;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_IntegerValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_IntegerValue = 10608;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_IntegerValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_IntegerValue_EURange = 10611;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UIntegerValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_UIntegerValue = 10614;

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UIntegerValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogScalar_UIntegerValue_EURange = 10617;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_SimulationActive Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_SimulationActive = 10621;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_GenerateValues_InputArguments = 10623;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_EventId = 10625;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_EventType = 10626;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_SourceNode = 10627;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_SourceName = 10628;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Time = 10629;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_ReceiveTime = 10630;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_LocalTime = 10631;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Message = 10632;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Severity = 10633;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionClassId = 11604;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionClassName = 11605;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_ConditionName = 11570;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_BranchId = 10634;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Retain = 10635;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_EnabledState = 10636;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_EnabledState_Id = 10637;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Quality = 10642;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Quality_SourceTimestamp = 10643;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_LastSeverity = 10646;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = 10647;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Comment = 10648;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Comment_SourceTimestamp = 10649;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_ClientUserId = 10650;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_AddComment_InputArguments = 10654;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_AckedState = 10657;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_AckedState_Id = 10658;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_ConfirmedState_Id = 10666;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Acknowledge_InputArguments = 10674;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_CycleComplete_Confirm_InputArguments = 10676;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_SByteValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_SByteValue = 10677;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_SByteValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_SByteValue_EURange = 10680;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_ByteValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_ByteValue = 10683;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_ByteValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_ByteValue_EURange = 10686;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int16Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_Int16Value = 10689;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int16Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_Int16Value_EURange = 10692;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt16Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_UInt16Value = 10695;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt16Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_UInt16Value_EURange = 10698;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int32Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_Int32Value = 10701;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int32Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_Int32Value_EURange = 10704;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt32Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_UInt32Value = 10707;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt32Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_UInt32Value_EURange = 10710;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int64Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_Int64Value = 10713;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int64Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_Int64Value_EURange = 10716;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt64Value Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_UInt64Value = 10719;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt64Value_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_UInt64Value_EURange = 10722;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_FloatValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_FloatValue = 10725;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_FloatValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_FloatValue_EURange = 10728;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_DoubleValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_DoubleValue = 10731;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_DoubleValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_DoubleValue_EURange = 10734;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_NumberValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_NumberValue = 10737;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_NumberValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_NumberValue_EURange = 10740;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_IntegerValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_IntegerValue = 10743;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_IntegerValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_IntegerValue_EURange = 10746;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UIntegerValue Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_UIntegerValue = 10749;

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UIntegerValue_EURange Variable.
        /// </summary>
        public const uint Data_Static_AnalogArray_UIntegerValue_EURange = 10752;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod1_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ScalarMethod1_InputArguments = 10757;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod1_OutputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ScalarMethod1_OutputArguments = 10758;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod2_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ScalarMethod2_InputArguments = 10760;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod2_OutputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ScalarMethod2_OutputArguments = 10761;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod3_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ScalarMethod3_InputArguments = 10763;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod3_OutputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ScalarMethod3_OutputArguments = 10764;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod1_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ArrayMethod1_InputArguments = 10766;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod1_OutputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ArrayMethod1_OutputArguments = 10767;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod2_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ArrayMethod2_InputArguments = 10769;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod2_OutputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ArrayMethod2_OutputArguments = 10770;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod3_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ArrayMethod3_InputArguments = 10772;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod3_OutputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_ArrayMethod3_OutputArguments = 10773;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod1_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_UserScalarMethod1_InputArguments = 10775;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod1_OutputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_UserScalarMethod1_OutputArguments = 10776;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod2_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_UserScalarMethod2_InputArguments = 10778;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod2_OutputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_UserScalarMethod2_OutputArguments = 10779;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod1_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_UserArrayMethod1_InputArguments = 10781;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod1_OutputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_UserArrayMethod1_OutputArguments = 10782;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod2_InputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_UserArrayMethod2_InputArguments = 10784;

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod2_OutputArguments Variable.
        /// </summary>
        public const uint Data_Static_MethodTest_UserArrayMethod2_OutputArguments = 10785;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_SimulationActive Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_SimulationActive = 10788;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_GenerateValues_InputArguments = 10790;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_EventId = 10792;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_EventType = 10793;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_SourceNode = 10794;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_SourceName = 10795;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Time = 10796;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_ReceiveTime = 10797;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_LocalTime = 10798;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Message = 10799;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Severity = 10800;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionClassId = 11606;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionClassName = 11607;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_ConditionName = 11571;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_BranchId = 10801;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Retain = 10802;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_EnabledState = 10803;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_EnabledState_Id = 10804;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Quality = 10809;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Quality_SourceTimestamp = 10810;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_LastSeverity = 10813;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_LastSeverity_SourceTimestamp = 10814;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Comment = 10815;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Comment_SourceTimestamp = 10816;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_ClientUserId = 10817;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_AddComment_InputArguments = 10821;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_AckedState = 10824;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_AckedState_Id = 10825;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_ConfirmedState_Id = 10833;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Acknowledge_InputArguments = 10841;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_CycleComplete_Confirm_InputArguments = 10843;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_BooleanValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_BooleanValue = 10844;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_SByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_SByteValue = 10845;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_ByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_ByteValue = 10846;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_Int16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_Int16Value = 10847;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_UInt16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_UInt16Value = 10848;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_Int32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_Int32Value = 10849;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_UInt32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_UInt32Value = 10850;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_Int64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_Int64Value = 10851;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_UInt64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_UInt64Value = 10852;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_FloatValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_FloatValue = 10853;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_DoubleValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_DoubleValue = 10854;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_StringValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_StringValue = 10855;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_DateTimeValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_DateTimeValue = 10856;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_GuidValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_GuidValue = 10857;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_ByteStringValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_ByteStringValue = 10858;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_XmlElementValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_XmlElementValue = 10859;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_NodeIdValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_NodeIdValue = 10860;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_ExpandedNodeIdValue = 10861;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_QualifiedNameValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_QualifiedNameValue = 10862;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_LocalizedTextValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_LocalizedTextValue = 10863;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_StatusCodeValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_StatusCodeValue = 10864;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_VariantValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_VariantValue = 10865;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_EnumerationValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_EnumerationValue = 10866;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_StructureValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_StructureValue = 10867;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_NumberValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_NumberValue = 10868;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_IntegerValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_IntegerValue = 10869;

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_UIntegerValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Scalar_UIntegerValue = 10870;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_SimulationActive Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_SimulationActive = 10872;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_GenerateValues_InputArguments = 10874;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_EventId = 10876;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_EventType = 10877;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_SourceNode = 10878;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_SourceName = 10879;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Time = 10880;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_ReceiveTime = 10881;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_LocalTime = 10882;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Message = 10883;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Severity = 10884;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_ConditionClassId = 11608;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_ConditionClassName = 11609;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_ConditionName = 11572;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_BranchId = 10885;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Retain = 10886;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_EnabledState = 10887;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_EnabledState_Id = 10888;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Quality = 10893;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Quality_SourceTimestamp = 10894;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_LastSeverity = 10897;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_LastSeverity_SourceTimestamp = 10898;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Comment = 10899;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Comment_SourceTimestamp = 10900;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_ClientUserId = 10901;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_AddComment_InputArguments = 10905;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_AckedState = 10908;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_AckedState_Id = 10909;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_ConfirmedState_Id = 10917;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Acknowledge_InputArguments = 10925;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_CycleComplete_Confirm_InputArguments = 10927;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_BooleanValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_BooleanValue = 10928;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_SByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_SByteValue = 10929;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_ByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_ByteValue = 10930;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_Int16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_Int16Value = 10931;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_UInt16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_UInt16Value = 10932;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_Int32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_Int32Value = 10933;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_UInt32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_UInt32Value = 10934;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_Int64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_Int64Value = 10935;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_UInt64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_UInt64Value = 10936;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_FloatValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_FloatValue = 10937;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_DoubleValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_DoubleValue = 10938;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_StringValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_StringValue = 10939;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_DateTimeValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_DateTimeValue = 10940;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_GuidValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_GuidValue = 10941;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_ByteStringValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_ByteStringValue = 10942;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_XmlElementValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_XmlElementValue = 10943;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_NodeIdValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_NodeIdValue = 10944;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_ExpandedNodeIdValue = 10945;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_QualifiedNameValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_QualifiedNameValue = 10946;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_LocalizedTextValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_LocalizedTextValue = 10947;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_StatusCodeValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_StatusCodeValue = 10948;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_VariantValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_VariantValue = 10949;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_EnumerationValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_EnumerationValue = 10950;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_StructureValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_StructureValue = 10951;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_NumberValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_NumberValue = 10952;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_IntegerValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_IntegerValue = 10953;

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_UIntegerValue Variable.
        /// </summary>
        public const uint Data_Dynamic_Array_UIntegerValue = 10954;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_SimulationActive Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_SimulationActive = 10956;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_GenerateValues_InputArguments = 10958;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_EventId = 10960;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_EventType = 10961;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_SourceNode = 10962;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_SourceName = 10963;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Time = 10964;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_ReceiveTime = 10965;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_LocalTime = 10966;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Message = 10967;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Severity = 10968;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionClassId = 11610;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionClassName = 11611;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConditionName = 11573;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_BranchId = 10969;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Retain = 10970;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_EnabledState = 10971;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_EnabledState_Id = 10972;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Quality = 10977;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Quality_SourceTimestamp = 10978;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_LastSeverity = 10981;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = 10982;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Comment = 10983;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Comment_SourceTimestamp = 10984;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_ClientUserId = 10985;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_AddComment_InputArguments = 10989;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_AckedState = 10992;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_AckedState_Id = 10993;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_ConfirmedState_Id = 11001;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Acknowledge_InputArguments = 11009;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_CycleComplete_Confirm_InputArguments = 11011;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_BooleanValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_BooleanValue = 11012;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_SByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_SByteValue = 11013;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_ByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_ByteValue = 11014;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_Int16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_Int16Value = 11015;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_UInt16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_UInt16Value = 11016;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_Int32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_Int32Value = 11017;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_UInt32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_UInt32Value = 11018;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_Int64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_Int64Value = 11019;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_UInt64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_UInt64Value = 11020;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_FloatValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_FloatValue = 11021;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_DoubleValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_DoubleValue = 11022;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_StringValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_StringValue = 11023;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_DateTimeValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_DateTimeValue = 11024;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_GuidValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_GuidValue = 11025;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_ByteStringValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_ByteStringValue = 11026;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_XmlElementValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_XmlElementValue = 11027;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_NodeIdValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_NodeIdValue = 11028;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_ExpandedNodeIdValue = 11029;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_QualifiedNameValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_QualifiedNameValue = 11030;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_LocalizedTextValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_LocalizedTextValue = 11031;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_StatusCodeValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_StatusCodeValue = 11032;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_VariantValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserScalar_VariantValue = 11033;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_SimulationActive Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_SimulationActive = 11035;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_GenerateValues_InputArguments = 11037;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_EventId = 11039;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_EventType = 11040;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_SourceNode = 11041;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_SourceName = 11042;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Time = 11043;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_ReceiveTime = 11044;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_LocalTime = 11045;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Message = 11046;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Severity = 11047;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionClassId = 11612;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionClassName = 11613;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_ConditionName = 11574;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_BranchId = 11048;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Retain = 11049;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_EnabledState = 11050;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_EnabledState_Id = 11051;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Quality = 11056;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Quality_SourceTimestamp = 11057;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_LastSeverity = 11060;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_LastSeverity_SourceTimestamp = 11061;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Comment = 11062;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Comment_SourceTimestamp = 11063;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_ClientUserId = 11064;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_AddComment_InputArguments = 11068;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_AckedState = 11071;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_AckedState_Id = 11072;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_ConfirmedState_Id = 11080;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Acknowledge_InputArguments = 11088;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_CycleComplete_Confirm_InputArguments = 11090;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_BooleanValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_BooleanValue = 11091;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_SByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_SByteValue = 11092;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_ByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_ByteValue = 11093;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_Int16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_Int16Value = 11094;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_UInt16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_UInt16Value = 11095;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_Int32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_Int32Value = 11096;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_UInt32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_UInt32Value = 11097;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_Int64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_Int64Value = 11098;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_UInt64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_UInt64Value = 11099;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_FloatValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_FloatValue = 11100;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_DoubleValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_DoubleValue = 11101;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_StringValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_StringValue = 11102;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_DateTimeValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_DateTimeValue = 11103;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_GuidValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_GuidValue = 11104;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_ByteStringValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_ByteStringValue = 11105;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_XmlElementValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_XmlElementValue = 11106;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_NodeIdValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_NodeIdValue = 11107;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_ExpandedNodeIdValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_ExpandedNodeIdValue = 11108;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_QualifiedNameValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_QualifiedNameValue = 11109;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_LocalizedTextValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_LocalizedTextValue = 11110;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_StatusCodeValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_StatusCodeValue = 11111;

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_VariantValue Variable.
        /// </summary>
        public const uint Data_Dynamic_UserArray_VariantValue = 11112;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_SimulationActive Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_SimulationActive = 11114;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_GenerateValues_InputArguments = 11116;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EventId = 11118;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EventType = 11119;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_SourceNode = 11120;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_SourceName = 11121;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Time = 11122;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ReceiveTime = 11123;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_LocalTime = 11124;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Message = 11125;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Severity = 11126;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassId = 11614;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassName = 11615;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConditionName = 11575;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_BranchId = 11127;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Retain = 11128;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EnabledState = 11129;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_EnabledState_Id = 11130;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Quality = 11135;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Quality_SourceTimestamp = 11136;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity = 11139;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = 11140;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Comment = 11141;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Comment_SourceTimestamp = 11142;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ClientUserId = 11143;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AddComment_InputArguments = 11147;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AckedState = 11150;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_AckedState_Id = 11151;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_ConfirmedState_Id = 11159;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge_InputArguments = 11167;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_CycleComplete_Confirm_InputArguments = 11169;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_SByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_SByteValue = 11170;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_SByteValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_SByteValue_EURange = 11173;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_ByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_ByteValue = 11176;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_ByteValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_ByteValue_EURange = 11179;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_Int16Value = 11182;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int16Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_Int16Value_EURange = 11185;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_UInt16Value = 11188;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt16Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_UInt16Value_EURange = 11191;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_Int32Value = 11194;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int32Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_Int32Value_EURange = 11197;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_UInt32Value = 11200;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt32Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_UInt32Value_EURange = 11203;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_Int64Value = 11206;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int64Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_Int64Value_EURange = 11209;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_UInt64Value = 11212;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt64Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_UInt64Value_EURange = 11215;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_FloatValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_FloatValue = 11218;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_FloatValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_FloatValue_EURange = 11221;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_DoubleValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_DoubleValue = 11224;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_DoubleValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_DoubleValue_EURange = 11227;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_NumberValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_NumberValue = 11230;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_NumberValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_NumberValue_EURange = 11233;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_IntegerValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_IntegerValue = 11236;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_IntegerValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_IntegerValue_EURange = 11239;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UIntegerValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_UIntegerValue = 11242;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UIntegerValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogScalar_UIntegerValue_EURange = 11245;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_SimulationActive Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_SimulationActive = 11249;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_GenerateValues_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_GenerateValues_InputArguments = 11251;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_EventId Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EventId = 11253;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_EventType Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EventType = 11254;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_SourceNode Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_SourceNode = 11255;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_SourceName Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_SourceName = 11256;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Time Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Time = 11257;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ReceiveTime = 11258;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_LocalTime Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_LocalTime = 11259;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Message Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Message = 11260;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Severity Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Severity = 11261;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionClassId = 11616;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionClassName = 11617;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ConditionName Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConditionName = 11576;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_BranchId Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_BranchId = 11262;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Retain Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Retain = 11263;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_EnabledState Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EnabledState = 11264;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_EnabledState_Id = 11265;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Quality Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Quality = 11270;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Quality_SourceTimestamp = 11271;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_LastSeverity Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_LastSeverity = 11274;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = 11275;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Comment Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Comment = 11276;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Comment_SourceTimestamp = 11277;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ClientUserId Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ClientUserId = 11278;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AddComment_InputArguments = 11282;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_AckedState Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AckedState = 11285;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_AckedState_Id = 11286;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_ConfirmedState_Id = 11294;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Acknowledge_InputArguments = 11302;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_CycleComplete_Confirm_InputArguments = 11304;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_SByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_SByteValue = 11305;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_SByteValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_SByteValue_EURange = 11308;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_ByteValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_ByteValue = 11311;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_ByteValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_ByteValue_EURange = 11314;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_Int16Value = 11317;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int16Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_Int16Value_EURange = 11320;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt16Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_UInt16Value = 11323;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt16Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_UInt16Value_EURange = 11326;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_Int32Value = 11329;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int32Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_Int32Value_EURange = 11332;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt32Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_UInt32Value = 11335;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt32Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_UInt32Value_EURange = 11338;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_Int64Value = 11341;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int64Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_Int64Value_EURange = 11344;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt64Value Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_UInt64Value = 11347;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt64Value_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_UInt64Value_EURange = 11350;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_FloatValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_FloatValue = 11353;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_FloatValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_FloatValue_EURange = 11356;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_DoubleValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_DoubleValue = 11359;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_DoubleValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_DoubleValue_EURange = 11362;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_NumberValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_NumberValue = 11365;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_NumberValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_NumberValue_EURange = 11368;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_IntegerValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_IntegerValue = 11371;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_IntegerValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_IntegerValue_EURange = 11374;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UIntegerValue Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_UIntegerValue = 11377;

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UIntegerValue_EURange Variable.
        /// </summary>
        public const uint Data_Dynamic_AnalogArray_UIntegerValue_EURange = 11380;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_EventId Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_EventId = 11385;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_EventType Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_EventType = 11386;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_SourceNode Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_SourceNode = 11387;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_SourceName Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_SourceName = 11388;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Time Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_Time = 11389;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_ReceiveTime Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_ReceiveTime = 11390;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_LocalTime Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_LocalTime = 11391;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Message Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_Message = 11392;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Severity Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_Severity = 11393;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_ConditionClassId Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_ConditionClassId = 11618;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_ConditionClassName Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_ConditionClassName = 11619;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_ConditionName Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_ConditionName = 11577;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_BranchId Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_BranchId = 11394;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Retain Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_Retain = 11395;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_EnabledState Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_EnabledState = 11396;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_EnabledState_Id Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_EnabledState_Id = 11397;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Quality Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_Quality = 11402;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Quality_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_Quality_SourceTimestamp = 11403;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_LastSeverity Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_LastSeverity = 11406;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_LastSeverity_SourceTimestamp = 11407;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Comment Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_Comment = 11408;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Comment_SourceTimestamp Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_Comment_SourceTimestamp = 11409;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_ClientUserId Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_ClientUserId = 11410;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_AddComment_InputArguments Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_AddComment_InputArguments = 11414;

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_MonitoredNodeCount Variable.
        /// </summary>
        public const uint Data_Conditions_SystemStatus_MonitoredNodeCount = 11417;

        /// <summary>
        /// The identifier for the TestData_XmlSchema Variable.
        /// </summary>
        public const uint TestData_XmlSchema = 11441;

        /// <summary>
        /// The identifier for the TestData_XmlSchema_NamespaceUri Variable.
        /// </summary>
        public const uint TestData_XmlSchema_NamespaceUri = 11443;

        /// <summary>
        /// The identifier for the TestData_XmlSchema_ScalarValueDataType Variable.
        /// </summary>
        public const uint TestData_XmlSchema_ScalarValueDataType = 11444;

        /// <summary>
        /// The identifier for the TestData_XmlSchema_ArrayValueDataType Variable.
        /// </summary>
        public const uint TestData_XmlSchema_ArrayValueDataType = 11447;

        /// <summary>
        /// The identifier for the TestData_XmlSchema_UserScalarValueDataType Variable.
        /// </summary>
        public const uint TestData_XmlSchema_UserScalarValueDataType = 11450;

        /// <summary>
        /// The identifier for the TestData_XmlSchema_UserArrayValueDataType Variable.
        /// </summary>
        public const uint TestData_XmlSchema_UserArrayValueDataType = 11453;

        /// <summary>
        /// The identifier for the TestData_BinarySchema Variable.
        /// </summary>
        public const uint TestData_BinarySchema = 11422;

        /// <summary>
        /// The identifier for the TestData_BinarySchema_NamespaceUri Variable.
        /// </summary>
        public const uint TestData_BinarySchema_NamespaceUri = 11424;

        /// <summary>
        /// The identifier for the TestData_BinarySchema_ScalarValueDataType Variable.
        /// </summary>
        public const uint TestData_BinarySchema_ScalarValueDataType = 11425;

        /// <summary>
        /// The identifier for the TestData_BinarySchema_ArrayValueDataType Variable.
        /// </summary>
        public const uint TestData_BinarySchema_ArrayValueDataType = 11428;

        /// <summary>
        /// The identifier for the TestData_BinarySchema_UserScalarValueDataType Variable.
        /// </summary>
        public const uint TestData_BinarySchema_UserScalarValueDataType = 11431;

        /// <summary>
        /// The identifier for the TestData_BinarySchema_UserArrayValueDataType Variable.
        /// </summary>
        public const uint TestData_BinarySchema_UserArrayValueDataType = 11434;
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
        /// The identifier for the ScalarValueDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueDataType = new ExpandedNodeId(TestData.DataTypes.ScalarValueDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueDataType = new ExpandedNodeId(TestData.DataTypes.ArrayValueDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the BooleanDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId BooleanDataType = new ExpandedNodeId(TestData.DataTypes.BooleanDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the SByteDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId SByteDataType = new ExpandedNodeId(TestData.DataTypes.SByteDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ByteDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId ByteDataType = new ExpandedNodeId(TestData.DataTypes.ByteDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Int16DataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId Int16DataType = new ExpandedNodeId(TestData.DataTypes.Int16DataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UInt16DataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId UInt16DataType = new ExpandedNodeId(TestData.DataTypes.UInt16DataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Int32DataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId Int32DataType = new ExpandedNodeId(TestData.DataTypes.Int32DataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UInt32DataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId UInt32DataType = new ExpandedNodeId(TestData.DataTypes.UInt32DataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Int64DataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId Int64DataType = new ExpandedNodeId(TestData.DataTypes.Int64DataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UInt64DataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId UInt64DataType = new ExpandedNodeId(TestData.DataTypes.UInt64DataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the FloatDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId FloatDataType = new ExpandedNodeId(TestData.DataTypes.FloatDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the DoubleDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId DoubleDataType = new ExpandedNodeId(TestData.DataTypes.DoubleDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the StringDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId StringDataType = new ExpandedNodeId(TestData.DataTypes.StringDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the DateTimeDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId DateTimeDataType = new ExpandedNodeId(TestData.DataTypes.DateTimeDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the GuidDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId GuidDataType = new ExpandedNodeId(TestData.DataTypes.GuidDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ByteStringDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId ByteStringDataType = new ExpandedNodeId(TestData.DataTypes.ByteStringDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the XmlElementDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId XmlElementDataType = new ExpandedNodeId(TestData.DataTypes.XmlElementDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the NodeIdDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId NodeIdDataType = new ExpandedNodeId(TestData.DataTypes.NodeIdDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ExpandedNodeIdDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId ExpandedNodeIdDataType = new ExpandedNodeId(TestData.DataTypes.ExpandedNodeIdDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the QualifiedNameDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId QualifiedNameDataType = new ExpandedNodeId(TestData.DataTypes.QualifiedNameDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the LocalizedTextDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId LocalizedTextDataType = new ExpandedNodeId(TestData.DataTypes.LocalizedTextDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the StatusCodeDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId StatusCodeDataType = new ExpandedNodeId(TestData.DataTypes.StatusCodeDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the VariantDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId VariantDataType = new ExpandedNodeId(TestData.DataTypes.VariantDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueDataType = new ExpandedNodeId(TestData.DataTypes.UserScalarValueDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueDataType DataType.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueDataType = new ExpandedNodeId(TestData.DataTypes.UserArrayValueDataType, TestData.Namespaces.TestData);
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
        /// The identifier for the GenerateValuesMethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId GenerateValuesMethodType = new ExpandedNodeId(TestData.Methods.GenerateValuesMethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestDataObjectType_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId TestDataObjectType_GenerateValues = new ExpandedNodeId(TestData.Methods.TestDataObjectType_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestDataObjectType_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.TestDataObjectType_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValue1MethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValue1MethodType = new ExpandedNodeId(TestData.Methods.ScalarValue1MethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValue2MethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValue2MethodType = new ExpandedNodeId(TestData.Methods.ScalarValue2MethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValue3MethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValue3MethodType = new ExpandedNodeId(TestData.Methods.ScalarValue3MethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValue1MethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValue1MethodType = new ExpandedNodeId(TestData.Methods.ArrayValue1MethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValue2MethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValue2MethodType = new ExpandedNodeId(TestData.Methods.ArrayValue2MethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValue3MethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValue3MethodType = new ExpandedNodeId(TestData.Methods.ArrayValue3MethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValue1MethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValue1MethodType = new ExpandedNodeId(TestData.Methods.UserScalarValue1MethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValue2MethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValue2MethodType = new ExpandedNodeId(TestData.Methods.UserScalarValue2MethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValue1MethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValue1MethodType = new ExpandedNodeId(TestData.Methods.UserArrayValue1MethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValue2MethodType Method.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValue2MethodType = new ExpandedNodeId(TestData.Methods.UserArrayValue2MethodType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod1 Method.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod1 = new ExpandedNodeId(TestData.Methods.MethodTestType_ScalarMethod1, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod2 Method.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod2 = new ExpandedNodeId(TestData.Methods.MethodTestType_ScalarMethod2, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod3 Method.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod3 = new ExpandedNodeId(TestData.Methods.MethodTestType_ScalarMethod3, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod1 Method.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod1 = new ExpandedNodeId(TestData.Methods.MethodTestType_ArrayMethod1, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod2 Method.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod2 = new ExpandedNodeId(TestData.Methods.MethodTestType_ArrayMethod2, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod3 Method.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod3 = new ExpandedNodeId(TestData.Methods.MethodTestType_ArrayMethod3, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod1 Method.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod1 = new ExpandedNodeId(TestData.Methods.MethodTestType_UserScalarMethod1, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod2 Method.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod2 = new ExpandedNodeId(TestData.Methods.MethodTestType_UserScalarMethod2, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod1 Method.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod1 = new ExpandedNodeId(TestData.Methods.MethodTestType_UserArrayMethod1, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod2 Method.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod2 = new ExpandedNodeId(TestData.Methods.MethodTestType_UserArrayMethod2, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_Scalar_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_Scalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_Scalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_Scalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_Scalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_Array_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_Array_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_Array_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_Array_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_Array_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_UserScalar_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_UserScalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_UserScalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_UserScalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_UserScalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_UserArray_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_UserArray_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_UserArray_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_UserArray_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_UserArray_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogScalar_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogScalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogScalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogScalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogScalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogArray_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogArray_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogArray_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogArray_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Static_AnalogArray_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod1 Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod1 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ScalarMethod1, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod2 Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod2 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ScalarMethod2, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod3 Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod3 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ScalarMethod3, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod1 Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod1 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ArrayMethod1, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod2 Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod2 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ArrayMethod2, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod3 Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod3 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_ArrayMethod3, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod1 Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod1 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_UserScalarMethod1, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod2 Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod2 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_UserScalarMethod2, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod1 Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod1 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_UserArrayMethod1, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod2 Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod2 = new ExpandedNodeId(TestData.Methods.Data_Static_MethodTest_UserArrayMethod2, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Scalar_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Scalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Scalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Scalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Scalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Array_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Array_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Array_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Array_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_Array_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserScalar_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserScalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserScalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserScalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserScalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserArray_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserArray_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserArray_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserArray_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_UserArray_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogScalar_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogScalar_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogScalar_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogScalar_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_GenerateValues Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_GenerateValues = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogArray_GenerateValues, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Disable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogArray_CycleComplete_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Enable = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogArray_CycleComplete_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_AddComment = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogArray_CycleComplete_AddComment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Acknowledge Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Acknowledge = new ExpandedNodeId(TestData.Methods.Data_Dynamic_AnalogArray_CycleComplete_Acknowledge, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Disable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Disable = new ExpandedNodeId(TestData.Methods.Data_Conditions_SystemStatus_Disable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Enable Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Enable = new ExpandedNodeId(TestData.Methods.Data_Conditions_SystemStatus_Enable, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_AddComment Method.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_AddComment = new ExpandedNodeId(TestData.Methods.Data_Conditions_SystemStatus_AddComment, TestData.Namespaces.TestData);
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
        /// The identifier for the TestDataObjectType_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete = new ExpandedNodeId(TestData.Objects.TestDataObjectType_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data Object.
        /// </summary>
        public static readonly ExpandedNodeId Data = new ExpandedNodeId(TestData.Objects.Data, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static = new ExpandedNodeId(TestData.Objects.Data_Static, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar = new ExpandedNodeId(TestData.Objects.Data_Static_Scalar, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_Scalar_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array = new ExpandedNodeId(TestData.Objects.Data_Static_Array, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_Array_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar = new ExpandedNodeId(TestData.Objects.Data_Static_UserScalar, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_UserScalar_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray = new ExpandedNodeId(TestData.Objects.Data_Static_UserArray, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_UserArray_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar = new ExpandedNodeId(TestData.Objects.Data_Static_AnalogScalar, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_AnalogScalar_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray = new ExpandedNodeId(TestData.Objects.Data_Static_AnalogArray, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Static_AnalogArray_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest = new ExpandedNodeId(TestData.Objects.Data_Static_MethodTest, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic = new ExpandedNodeId(TestData.Objects.Data_Dynamic, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar = new ExpandedNodeId(TestData.Objects.Data_Dynamic_Scalar, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_Scalar_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array = new ExpandedNodeId(TestData.Objects.Data_Dynamic_Array, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_Array_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar = new ExpandedNodeId(TestData.Objects.Data_Dynamic_UserScalar, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_UserScalar_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray = new ExpandedNodeId(TestData.Objects.Data_Dynamic_UserArray, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_UserArray_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar = new ExpandedNodeId(TestData.Objects.Data_Dynamic_AnalogScalar, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_AnalogScalar_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray = new ExpandedNodeId(TestData.Objects.Data_Dynamic_AnalogArray, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete = new ExpandedNodeId(TestData.Objects.Data_Dynamic_AnalogArray_CycleComplete, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions = new ExpandedNodeId(TestData.Objects.Data_Conditions, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus Object.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus = new ExpandedNodeId(TestData.Objects.Data_Conditions_SystemStatus, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueDataType_Encoding_DefaultXml Object.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.ScalarValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueDataType_Encoding_DefaultXml Object.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.ArrayValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueDataType_Encoding_DefaultXml Object.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.UserScalarValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueDataType_Encoding_DefaultXml Object.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueDataType_Encoding_DefaultXml = new ExpandedNodeId(TestData.Objects.UserArrayValueDataType_Encoding_DefaultXml, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.ScalarValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.ArrayValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.UserScalarValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueDataType_Encoding_DefaultBinary Object.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueDataType_Encoding_DefaultBinary = new ExpandedNodeId(TestData.Objects.UserArrayValueDataType_Encoding_DefaultBinary, TestData.Namespaces.TestData);
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
        /// The identifier for the GenerateValuesEventType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId GenerateValuesEventType = new ExpandedNodeId(TestData.ObjectTypes.GenerateValuesEventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestDataObjectType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId TestDataObjectType = new ExpandedNodeId(TestData.ObjectTypes.TestDataObjectType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.ScalarValueObjectType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.AnalogScalarValueObjectType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.ArrayValueObjectType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.AnalogArrayValueObjectType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.UserScalarValueObjectType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType = new ExpandedNodeId(TestData.ObjectTypes.UserArrayValueObjectType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType = new ExpandedNodeId(TestData.ObjectTypes.MethodTestType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestSystemConditionType ObjectType.
        /// </summary>
        public static readonly ExpandedNodeId TestSystemConditionType = new ExpandedNodeId(TestData.ObjectTypes.TestSystemConditionType, TestData.Namespaces.TestData);
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
        /// The identifier for the GenerateValuesMethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenerateValuesMethodType_InputArguments = new ExpandedNodeId(TestData.Variables.GenerateValuesMethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the GenerateValuesEventType_Iterations Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenerateValuesEventType_Iterations = new ExpandedNodeId(TestData.Variables.GenerateValuesEventType_Iterations, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the GenerateValuesEventType_NewValueCount Variable.
        /// </summary>
        public static readonly ExpandedNodeId GenerateValuesEventType_NewValueCount = new ExpandedNodeId(TestData.Variables.GenerateValuesEventType_NewValueCount, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestDataObjectType_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestDataObjectType_SimulationActive = new ExpandedNodeId(TestData.Variables.TestDataObjectType_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestDataObjectType_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestDataObjectType_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataObjectType_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestDataObjectType_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestDataObjectType_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestDataObjectType_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestDataObjectType_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.TestDataObjectType_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValue1MethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValue1MethodType_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValue1MethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValue1MethodType_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValue1MethodType_OutputArguments = new ExpandedNodeId(TestData.Variables.ScalarValue1MethodType_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValue2MethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValue2MethodType_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValue2MethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValue2MethodType_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValue2MethodType_OutputArguments = new ExpandedNodeId(TestData.Variables.ScalarValue2MethodType_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValue3MethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValue3MethodType_InputArguments = new ExpandedNodeId(TestData.Variables.ScalarValue3MethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValue3MethodType_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValue3MethodType_OutputArguments = new ExpandedNodeId(TestData.Variables.ScalarValue3MethodType_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_BooleanValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_StringValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_DateTimeValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_GuidValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_ByteStringValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_XmlElementValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_NodeIdValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_StatusCodeValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_VariantValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_EnumerationValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_EnumerationValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_EnumerationValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_StructureValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_StructureValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_StructureValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_NumberValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_IntegerValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ScalarValueObjectType_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ScalarValueObjectType_UIntegerValue = new ExpandedNodeId(TestData.Variables.ScalarValueObjectType_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_SByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_ByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_Int64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UInt64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_FloatValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_DoubleValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_NumberValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_NumberValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_IntegerValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_IntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UIntegerValue = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogScalarValueObjectType_UIntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogScalarValueObjectType_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogScalarValueObjectType_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValue1MethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValue1MethodType_InputArguments = new ExpandedNodeId(TestData.Variables.ArrayValue1MethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValue1MethodType_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValue1MethodType_OutputArguments = new ExpandedNodeId(TestData.Variables.ArrayValue1MethodType_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValue2MethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValue2MethodType_InputArguments = new ExpandedNodeId(TestData.Variables.ArrayValue2MethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValue2MethodType_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValue2MethodType_OutputArguments = new ExpandedNodeId(TestData.Variables.ArrayValue2MethodType_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValue3MethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValue3MethodType_InputArguments = new ExpandedNodeId(TestData.Variables.ArrayValue3MethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValue3MethodType_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValue3MethodType_OutputArguments = new ExpandedNodeId(TestData.Variables.ArrayValue3MethodType_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_BooleanValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_StringValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_DateTimeValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_GuidValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_ByteStringValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_XmlElementValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_NodeIdValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_StatusCodeValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_VariantValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_EnumerationValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_EnumerationValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_EnumerationValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_StructureValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_StructureValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_StructureValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_NumberValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_IntegerValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the ArrayValueObjectType_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId ArrayValueObjectType_UIntegerValue = new ExpandedNodeId(TestData.Variables.ArrayValueObjectType_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_SByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_ByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_Int64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UInt64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_FloatValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_DoubleValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_NumberValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_NumberValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_IntegerValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_IntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UIntegerValue = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the AnalogArrayValueObjectType_UIntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId AnalogArrayValueObjectType_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.AnalogArrayValueObjectType_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_BooleanValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_StringValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_DateTimeValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_GuidValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_ByteStringValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_XmlElementValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_NodeIdValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_StatusCodeValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValueObjectType_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValueObjectType_VariantValue = new ExpandedNodeId(TestData.Variables.UserScalarValueObjectType_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValue1MethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValue1MethodType_InputArguments = new ExpandedNodeId(TestData.Variables.UserScalarValue1MethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValue1MethodType_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValue1MethodType_OutputArguments = new ExpandedNodeId(TestData.Variables.UserScalarValue1MethodType_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValue2MethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValue2MethodType_InputArguments = new ExpandedNodeId(TestData.Variables.UserScalarValue2MethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserScalarValue2MethodType_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserScalarValue2MethodType_OutputArguments = new ExpandedNodeId(TestData.Variables.UserScalarValue2MethodType_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_BooleanValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_SByteValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_ByteValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_Int16Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_UInt16Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_Int32Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_UInt32Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_Int64Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_UInt64Value = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_FloatValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_DoubleValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_StringValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_DateTimeValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_GuidValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_ByteStringValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_XmlElementValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_NodeIdValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_StatusCodeValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValueObjectType_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValueObjectType_VariantValue = new ExpandedNodeId(TestData.Variables.UserArrayValueObjectType_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValue1MethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValue1MethodType_InputArguments = new ExpandedNodeId(TestData.Variables.UserArrayValue1MethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValue1MethodType_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValue1MethodType_OutputArguments = new ExpandedNodeId(TestData.Variables.UserArrayValue1MethodType_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValue2MethodType_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValue2MethodType_InputArguments = new ExpandedNodeId(TestData.Variables.UserArrayValue2MethodType_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the UserArrayValue2MethodType_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId UserArrayValue2MethodType_OutputArguments = new ExpandedNodeId(TestData.Variables.UserArrayValue2MethodType_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod1_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod1_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod2_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod2_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod3_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod3_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod3_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ScalarMethod3_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ScalarMethod3_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ScalarMethod3_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod1_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod1_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod2_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod2_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod3_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod3_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod3_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_ArrayMethod3_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_ArrayMethod3_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_ArrayMethod3_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod1_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserScalarMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod1_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserScalarMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod2_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserScalarMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserScalarMethod2_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserScalarMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserScalarMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod1_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserArrayMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod1_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserArrayMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod2_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserArrayMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the MethodTestType_UserArrayMethod2_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId MethodTestType_UserArrayMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.MethodTestType_UserArrayMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestSystemConditionType_MonitoredNodeCount Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestSystemConditionType_MonitoredNodeCount = new ExpandedNodeId(TestData.Variables.TestSystemConditionType_MonitoredNodeCount, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_StringValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_EnumerationValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_EnumerationValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_StructureValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_StructureValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Scalar_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Scalar_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_Scalar_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_Array_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Array_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_Array_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_Array_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_StringValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_EnumerationValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_EnumerationValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_StructureValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_StructureValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_Array_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_Array_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_Array_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_StringValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserScalar_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserScalar_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserScalar_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_StringValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_UserArray_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_UserArray_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Static_UserArray_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_SByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_ByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_Int64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UInt64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_FloatValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_DoubleValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_NumberValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_IntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogScalar_UIntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogScalar_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogScalar_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_SByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_ByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_Int64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UInt64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_FloatValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_DoubleValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_NumberValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_IntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_AnalogArray_UIntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_AnalogArray_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Static_AnalogArray_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod1_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod1_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod2_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod2_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod3_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod3_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod3_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ScalarMethod3_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ScalarMethod3_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ScalarMethod3_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod1_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod1_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod2_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod2_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod3_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod3_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod3_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_ArrayMethod3_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_ArrayMethod3_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_ArrayMethod3_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod1_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserScalarMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod1_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserScalarMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod2_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserScalarMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserScalarMethod2_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserScalarMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserScalarMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod1_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod1_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserArrayMethod1_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod1_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod1_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserArrayMethod1_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod2_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod2_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserArrayMethod2_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Static_MethodTest_UserArrayMethod2_OutputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Static_MethodTest_UserArrayMethod2_OutputArguments = new ExpandedNodeId(TestData.Variables.Data_Static_MethodTest_UserArrayMethod2_OutputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_StringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_EnumerationValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_EnumerationValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_StructureValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_StructureValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Scalar_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Scalar_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Scalar_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_StringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_EnumerationValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_EnumerationValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_EnumerationValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_StructureValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_StructureValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_StructureValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_Array_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_Array_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_Array_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_StringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserScalar_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserScalar_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserScalar_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_BooleanValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_BooleanValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_BooleanValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_StringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_StringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_StringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_DateTimeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_DateTimeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_DateTimeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_GuidValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_GuidValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_GuidValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_ByteStringValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_ByteStringValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_ByteStringValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_XmlElementValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_XmlElementValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_XmlElementValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_NodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_NodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_NodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_ExpandedNodeIdValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_ExpandedNodeIdValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_ExpandedNodeIdValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_QualifiedNameValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_QualifiedNameValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_QualifiedNameValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_LocalizedTextValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_LocalizedTextValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_LocalizedTextValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_StatusCodeValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_StatusCodeValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_StatusCodeValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_UserArray_VariantValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_UserArray_VariantValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_UserArray_VariantValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_SByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_ByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_Int64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UInt64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_FloatValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_DoubleValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_NumberValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_IntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogScalar_UIntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogScalar_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogScalar_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_SimulationActive Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_SimulationActive = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_SimulationActive, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_GenerateValues_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_GenerateValues_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_GenerateValues_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_EventId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_EventType = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_SourceName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Time = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Message = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Severity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_BranchId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Retain = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Quality = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Comment = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_AckedState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_AckedState = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_AckedState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_AckedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_AckedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_AckedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_ConfirmedState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_ConfirmedState_Id = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_ConfirmedState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Acknowledge_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Acknowledge_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Acknowledge_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_CycleComplete_Confirm_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_CycleComplete_Confirm_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_CycleComplete_Confirm_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_SByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_SByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_SByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_SByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_SByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_SByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_ByteValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_ByteValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_ByteValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_ByteValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_ByteValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_ByteValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt16Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt16Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt16Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt16Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt16Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt16Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt32Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt32Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt32Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt32Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt32Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt32Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_Int64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_Int64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_Int64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt64Value Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt64Value = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt64Value, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UInt64Value_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UInt64Value_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UInt64Value_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_FloatValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_FloatValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_FloatValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_FloatValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_FloatValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_FloatValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_DoubleValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_DoubleValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_DoubleValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_DoubleValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_DoubleValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_DoubleValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_NumberValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_NumberValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_NumberValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_NumberValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_NumberValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_NumberValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_IntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_IntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_IntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_IntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_IntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_IntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UIntegerValue Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UIntegerValue = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UIntegerValue, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Dynamic_AnalogArray_UIntegerValue_EURange Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Dynamic_AnalogArray_UIntegerValue_EURange = new ExpandedNodeId(TestData.Variables.Data_Dynamic_AnalogArray_UIntegerValue_EURange, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_EventId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_EventId = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_EventId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_EventType Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_EventType = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_EventType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_SourceNode Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_SourceNode = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_SourceNode, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_SourceName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_SourceName = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_SourceName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Time Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Time = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Time, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_ReceiveTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_ReceiveTime = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_ReceiveTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_LocalTime Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_LocalTime = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_LocalTime, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Message Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Message = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Message, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Severity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Severity = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Severity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_ConditionClassId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_ConditionClassId = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_ConditionClassId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_ConditionClassName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_ConditionClassName = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_ConditionClassName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_ConditionName Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_ConditionName = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_ConditionName, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_BranchId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_BranchId = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_BranchId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Retain Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Retain = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Retain, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_EnabledState Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_EnabledState = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_EnabledState, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_EnabledState_Id Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_EnabledState_Id = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_EnabledState_Id, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Quality Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Quality = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Quality, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Quality_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Quality_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Quality_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_LastSeverity Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_LastSeverity = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_LastSeverity, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_LastSeverity_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_LastSeverity_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_LastSeverity_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Comment Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Comment = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Comment, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_Comment_SourceTimestamp Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_Comment_SourceTimestamp = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_Comment_SourceTimestamp, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_ClientUserId Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_ClientUserId = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_ClientUserId, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_AddComment_InputArguments Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_AddComment_InputArguments = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_AddComment_InputArguments, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the Data_Conditions_SystemStatus_MonitoredNodeCount Variable.
        /// </summary>
        public static readonly ExpandedNodeId Data_Conditions_SystemStatus_MonitoredNodeCount = new ExpandedNodeId(TestData.Variables.Data_Conditions_SystemStatus_MonitoredNodeCount, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_XmlSchema Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_XmlSchema = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_XmlSchema_NamespaceUri Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_XmlSchema_NamespaceUri = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_NamespaceUri, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_XmlSchema_ScalarValueDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_XmlSchema_ScalarValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_ScalarValueDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_XmlSchema_ArrayValueDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_XmlSchema_ArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_ArrayValueDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_XmlSchema_UserScalarValueDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_XmlSchema_UserScalarValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_UserScalarValueDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_XmlSchema_UserArrayValueDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_XmlSchema_UserArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_XmlSchema_UserArrayValueDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_BinarySchema Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_BinarySchema = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_BinarySchema_NamespaceUri Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_BinarySchema_NamespaceUri = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_NamespaceUri, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_BinarySchema_ScalarValueDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_BinarySchema_ScalarValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_ScalarValueDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_BinarySchema_ArrayValueDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_BinarySchema_ArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_ArrayValueDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_BinarySchema_UserScalarValueDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_BinarySchema_UserScalarValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_UserScalarValueDataType, TestData.Namespaces.TestData);

        /// <summary>
        /// The identifier for the TestData_BinarySchema_UserArrayValueDataType Variable.
        /// </summary>
        public static readonly ExpandedNodeId TestData_BinarySchema_UserArrayValueDataType = new ExpandedNodeId(TestData.Variables.TestData_BinarySchema_UserArrayValueDataType, TestData.Namespaces.TestData);
    }
    #endregion

    #region BrowseName Declarations
    /// <summary>
    /// Declares all of the BrowseNames used in the Model Design.
    /// </summary>
    public static partial class BrowseNames
    {
        /// <summary>
        /// The BrowseName for the AnalogArrayValueObjectType component.
        /// </summary>
        public const string AnalogArrayValueObjectType = "AnalogArrayValueObjectType";

        /// <summary>
        /// The BrowseName for the AnalogScalarValueObjectType component.
        /// </summary>
        public const string AnalogScalarValueObjectType = "AnalogScalarValueObjectType";

        /// <summary>
        /// The BrowseName for the ArrayMethod1 component.
        /// </summary>
        public const string ArrayMethod1 = "ArrayMethod1";

        /// <summary>
        /// The BrowseName for the ArrayMethod2 component.
        /// </summary>
        public const string ArrayMethod2 = "ArrayMethod2";

        /// <summary>
        /// The BrowseName for the ArrayMethod3 component.
        /// </summary>
        public const string ArrayMethod3 = "ArrayMethod3";

        /// <summary>
        /// The BrowseName for the ArrayValue1MethodType component.
        /// </summary>
        public const string ArrayValue1MethodType = "ArrayValue1MethodType";

        /// <summary>
        /// The BrowseName for the ArrayValue2MethodType component.
        /// </summary>
        public const string ArrayValue2MethodType = "ArrayValue2MethodType";

        /// <summary>
        /// The BrowseName for the ArrayValue3MethodType component.
        /// </summary>
        public const string ArrayValue3MethodType = "ArrayValue3MethodType";

        /// <summary>
        /// The BrowseName for the ArrayValueDataType component.
        /// </summary>
        public const string ArrayValueDataType = "ArrayValueDataType";

        /// <summary>
        /// The BrowseName for the ArrayValueObjectType component.
        /// </summary>
        public const string ArrayValueObjectType = "ArrayValueObjectType";

        /// <summary>
        /// The BrowseName for the BooleanDataType component.
        /// </summary>
        public const string BooleanDataType = "BooleanDataType";

        /// <summary>
        /// The BrowseName for the BooleanValue component.
        /// </summary>
        public const string BooleanValue = "BooleanValue";

        /// <summary>
        /// The BrowseName for the ByteDataType component.
        /// </summary>
        public const string ByteDataType = "ByteDataType";

        /// <summary>
        /// The BrowseName for the ByteStringDataType component.
        /// </summary>
        public const string ByteStringDataType = "ByteStringDataType";

        /// <summary>
        /// The BrowseName for the ByteStringValue component.
        /// </summary>
        public const string ByteStringValue = "ByteStringValue";

        /// <summary>
        /// The BrowseName for the ByteValue component.
        /// </summary>
        public const string ByteValue = "ByteValue";

        /// <summary>
        /// The BrowseName for the Conditions component.
        /// </summary>
        public const string Conditions = "Conditions";

        /// <summary>
        /// The BrowseName for the CycleComplete component.
        /// </summary>
        public const string CycleComplete = "CycleComplete";

        /// <summary>
        /// The BrowseName for the Data component.
        /// </summary>
        public const string Data = "Data";

        /// <summary>
        /// The BrowseName for the DateTimeDataType component.
        /// </summary>
        public const string DateTimeDataType = "DateTimeDataType";

        /// <summary>
        /// The BrowseName for the DateTimeValue component.
        /// </summary>
        public const string DateTimeValue = "DateTimeValue";

        /// <summary>
        /// The BrowseName for the DoubleDataType component.
        /// </summary>
        public const string DoubleDataType = "DoubleDataType";

        /// <summary>
        /// The BrowseName for the DoubleValue component.
        /// </summary>
        public const string DoubleValue = "DoubleValue";

        /// <summary>
        /// The BrowseName for the Dynamic component.
        /// </summary>
        public const string Dynamic = "Dynamic";

        /// <summary>
        /// The BrowseName for the EnumerationValue component.
        /// </summary>
        public const string EnumerationValue = "EnumerationValue";

        /// <summary>
        /// The BrowseName for the ExpandedNodeIdDataType component.
        /// </summary>
        public const string ExpandedNodeIdDataType = "ExpandedNodeIdDataType";

        /// <summary>
        /// The BrowseName for the ExpandedNodeIdValue component.
        /// </summary>
        public const string ExpandedNodeIdValue = "ExpandedNodeIdValue";

        /// <summary>
        /// The BrowseName for the FloatDataType component.
        /// </summary>
        public const string FloatDataType = "FloatDataType";

        /// <summary>
        /// The BrowseName for the FloatValue component.
        /// </summary>
        public const string FloatValue = "FloatValue";

        /// <summary>
        /// The BrowseName for the GenerateValues component.
        /// </summary>
        public const string GenerateValues = "GenerateValues";

        /// <summary>
        /// The BrowseName for the GenerateValuesEventType component.
        /// </summary>
        public const string GenerateValuesEventType = "GenerateValuesEventType";

        /// <summary>
        /// The BrowseName for the GenerateValuesMethodType component.
        /// </summary>
        public const string GenerateValuesMethodType = "GenerateValuesMethodType";

        /// <summary>
        /// The BrowseName for the GuidDataType component.
        /// </summary>
        public const string GuidDataType = "GuidDataType";

        /// <summary>
        /// The BrowseName for the GuidValue component.
        /// </summary>
        public const string GuidValue = "GuidValue";

        /// <summary>
        /// The BrowseName for the Int16DataType component.
        /// </summary>
        public const string Int16DataType = "Int16DataType";

        /// <summary>
        /// The BrowseName for the Int16Value component.
        /// </summary>
        public const string Int16Value = "Int16Value";

        /// <summary>
        /// The BrowseName for the Int32DataType component.
        /// </summary>
        public const string Int32DataType = "Int32DataType";

        /// <summary>
        /// The BrowseName for the Int32Value component.
        /// </summary>
        public const string Int32Value = "Int32Value";

        /// <summary>
        /// The BrowseName for the Int64DataType component.
        /// </summary>
        public const string Int64DataType = "Int64DataType";

        /// <summary>
        /// The BrowseName for the Int64Value component.
        /// </summary>
        public const string Int64Value = "Int64Value";

        /// <summary>
        /// The BrowseName for the IntegerValue component.
        /// </summary>
        public const string IntegerValue = "IntegerValue";

        /// <summary>
        /// The BrowseName for the Iterations component.
        /// </summary>
        public const string Iterations = "Iterations";

        /// <summary>
        /// The BrowseName for the LocalizedTextDataType component.
        /// </summary>
        public const string LocalizedTextDataType = "LocalizedTextDataType";

        /// <summary>
        /// The BrowseName for the LocalizedTextValue component.
        /// </summary>
        public const string LocalizedTextValue = "LocalizedTextValue";

        /// <summary>
        /// The BrowseName for the MethodTestType component.
        /// </summary>
        public const string MethodTestType = "MethodTestType";

        /// <summary>
        /// The BrowseName for the MonitoredNodeCount component.
        /// </summary>
        public const string MonitoredNodeCount = "MonitoredNodeCount";

        /// <summary>
        /// The BrowseName for the NewValueCount component.
        /// </summary>
        public const string NewValueCount = "NewValueCount";

        /// <summary>
        /// The BrowseName for the NodeIdDataType component.
        /// </summary>
        public const string NodeIdDataType = "NodeIdDataType";

        /// <summary>
        /// The BrowseName for the NodeIdValue component.
        /// </summary>
        public const string NodeIdValue = "NodeIdValue";

        /// <summary>
        /// The BrowseName for the NumberValue component.
        /// </summary>
        public const string NumberValue = "NumberValue";

        /// <summary>
        /// The BrowseName for the QualifiedNameDataType component.
        /// </summary>
        public const string QualifiedNameDataType = "QualifiedNameDataType";

        /// <summary>
        /// The BrowseName for the QualifiedNameValue component.
        /// </summary>
        public const string QualifiedNameValue = "QualifiedNameValue";

        /// <summary>
        /// The BrowseName for the SByteDataType component.
        /// </summary>
        public const string SByteDataType = "SByteDataType";

        /// <summary>
        /// The BrowseName for the SByteValue component.
        /// </summary>
        public const string SByteValue = "SByteValue";

        /// <summary>
        /// The BrowseName for the ScalarMethod1 component.
        /// </summary>
        public const string ScalarMethod1 = "ScalarMethod1";

        /// <summary>
        /// The BrowseName for the ScalarMethod2 component.
        /// </summary>
        public const string ScalarMethod2 = "ScalarMethod2";

        /// <summary>
        /// The BrowseName for the ScalarMethod3 component.
        /// </summary>
        public const string ScalarMethod3 = "ScalarMethod3";

        /// <summary>
        /// The BrowseName for the ScalarValue1MethodType component.
        /// </summary>
        public const string ScalarValue1MethodType = "ScalarValue1MethodType";

        /// <summary>
        /// The BrowseName for the ScalarValue2MethodType component.
        /// </summary>
        public const string ScalarValue2MethodType = "ScalarValue2MethodType";

        /// <summary>
        /// The BrowseName for the ScalarValue3MethodType component.
        /// </summary>
        public const string ScalarValue3MethodType = "ScalarValue3MethodType";

        /// <summary>
        /// The BrowseName for the ScalarValueDataType component.
        /// </summary>
        public const string ScalarValueDataType = "ScalarValueDataType";

        /// <summary>
        /// The BrowseName for the ScalarValueObjectType component.
        /// </summary>
        public const string ScalarValueObjectType = "ScalarValueObjectType";

        /// <summary>
        /// The BrowseName for the SimulationActive component.
        /// </summary>
        public const string SimulationActive = "SimulationActive";

        /// <summary>
        /// The BrowseName for the Static component.
        /// </summary>
        public const string Static = "Static";

        /// <summary>
        /// The BrowseName for the StatusCodeDataType component.
        /// </summary>
        public const string StatusCodeDataType = "StatusCodeDataType";

        /// <summary>
        /// The BrowseName for the StatusCodeValue component.
        /// </summary>
        public const string StatusCodeValue = "StatusCodeValue";

        /// <summary>
        /// The BrowseName for the StringDataType component.
        /// </summary>
        public const string StringDataType = "StringDataType";

        /// <summary>
        /// The BrowseName for the StringValue component.
        /// </summary>
        public const string StringValue = "StringValue";

        /// <summary>
        /// The BrowseName for the StructureValue component.
        /// </summary>
        public const string StructureValue = "StructureValue";

        /// <summary>
        /// The BrowseName for the TestData_BinarySchema component.
        /// </summary>
        public const string TestData_BinarySchema = "TestData";

        /// <summary>
        /// The BrowseName for the TestData_XmlSchema component.
        /// </summary>
        public const string TestData_XmlSchema = "TestData";

        /// <summary>
        /// The BrowseName for the TestDataObjectType component.
        /// </summary>
        public const string TestDataObjectType = "TestDataObjectType";

        /// <summary>
        /// The BrowseName for the TestSystemConditionType component.
        /// </summary>
        public const string TestSystemConditionType = "TestSystemConditionType";

        /// <summary>
        /// The BrowseName for the UInt16DataType component.
        /// </summary>
        public const string UInt16DataType = "UInt16DataType";

        /// <summary>
        /// The BrowseName for the UInt16Value component.
        /// </summary>
        public const string UInt16Value = "UInt16Value";

        /// <summary>
        /// The BrowseName for the UInt32DataType component.
        /// </summary>
        public const string UInt32DataType = "UInt32DataType";

        /// <summary>
        /// The BrowseName for the UInt32Value component.
        /// </summary>
        public const string UInt32Value = "UInt32Value";

        /// <summary>
        /// The BrowseName for the UInt64DataType component.
        /// </summary>
        public const string UInt64DataType = "UInt64DataType";

        /// <summary>
        /// The BrowseName for the UInt64Value component.
        /// </summary>
        public const string UInt64Value = "UInt64Value";

        /// <summary>
        /// The BrowseName for the UIntegerValue component.
        /// </summary>
        public const string UIntegerValue = "UIntegerValue";

        /// <summary>
        /// The BrowseName for the UserArrayMethod1 component.
        /// </summary>
        public const string UserArrayMethod1 = "UserArrayMethod1";

        /// <summary>
        /// The BrowseName for the UserArrayMethod2 component.
        /// </summary>
        public const string UserArrayMethod2 = "UserArrayMethod2";

        /// <summary>
        /// The BrowseName for the UserArrayValue1MethodType component.
        /// </summary>
        public const string UserArrayValue1MethodType = "UserArrayValue1MethodType";

        /// <summary>
        /// The BrowseName for the UserArrayValue2MethodType component.
        /// </summary>
        public const string UserArrayValue2MethodType = "UserArrayValue2MethodType";

        /// <summary>
        /// The BrowseName for the UserArrayValueDataType component.
        /// </summary>
        public const string UserArrayValueDataType = "UserArrayValueDataType";

        /// <summary>
        /// The BrowseName for the UserArrayValueObjectType component.
        /// </summary>
        public const string UserArrayValueObjectType = "UserArrayValueObjectType";

        /// <summary>
        /// The BrowseName for the UserScalarMethod1 component.
        /// </summary>
        public const string UserScalarMethod1 = "UserScalarMethod1";

        /// <summary>
        /// The BrowseName for the UserScalarMethod2 component.
        /// </summary>
        public const string UserScalarMethod2 = "UserScalarMethod2";

        /// <summary>
        /// The BrowseName for the UserScalarValue1MethodType component.
        /// </summary>
        public const string UserScalarValue1MethodType = "UserScalarValue1MethodType";

        /// <summary>
        /// The BrowseName for the UserScalarValue2MethodType component.
        /// </summary>
        public const string UserScalarValue2MethodType = "UserScalarValue2MethodType";

        /// <summary>
        /// The BrowseName for the UserScalarValueDataType component.
        /// </summary>
        public const string UserScalarValueDataType = "UserScalarValueDataType";

        /// <summary>
        /// The BrowseName for the UserScalarValueObjectType component.
        /// </summary>
        public const string UserScalarValueObjectType = "UserScalarValueObjectType";

        /// <summary>
        /// The BrowseName for the VariantDataType component.
        /// </summary>
        public const string VariantDataType = "VariantDataType";

        /// <summary>
        /// The BrowseName for the VariantValue component.
        /// </summary>
        public const string VariantValue = "VariantValue";

        /// <summary>
        /// The BrowseName for the XmlElementDataType component.
        /// </summary>
        public const string XmlElementDataType = "XmlElementDataType";

        /// <summary>
        /// The BrowseName for the XmlElementValue component.
        /// </summary>
        public const string XmlElementValue = "XmlElementValue";
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
        /// The URI for the TestData namespace (.NET code namespace is 'TestData').
        /// </summary>
        public const string TestData = "http://test.org/UA/Data/";
    }
    #endregion
}