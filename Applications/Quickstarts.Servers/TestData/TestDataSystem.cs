/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace TestData
{
    public interface ITestDataSystemCallback
    {
        void OnDataChange(
            BaseVariableState variable,
            Variant value,
            StatusCode statusCode,
            DateTime timestamp);

        void OnGenerateValues(BaseVariableState variable);
    }

    public interface ITestDataSystemValuesGenerator
    {
        StatusCode OnGenerateValues(ISystemContext context);
    }

    public class TestDataSystem
    {
        public TestDataSystem(
            ITestDataSystemCallback callback,
            NamespaceTable namespaceUris,
            StringTable serverUris,
            ITelemetryContext telemetry)
        {
            m_callback = callback;
            m_logger = telemetry.CreateLogger<TestDataSystem>();
            m_minimumSamplingInterval = int.MaxValue;
            m_monitoredNodes = [];
            m_samplingNodes = null;
            Generator = new Opc.Ua.Test.DataGenerator(null, telemetry)
            {
                NamespaceUris = namespaceUris,
                ServerUris = serverUris
            };
            m_historyArchive = new HistoryArchive(telemetry);
        }

        /// <summary>
        /// The number of nodes being monitored.
        /// </summary>
        public int MonitoredNodeCount
        {
            get
            {
                lock (m_lock)
                {
                    if (m_monitoredNodes == null)
                    {
                        return 0;
                    }

                    return m_monitoredNodes.Count;
                }
            }
        }

        /// <summary>
        /// Gets or sets the current system status.
        /// </summary>
        public StatusCode SystemStatus
        {
            get
            {
                lock (m_lock)
                {
                    return m_systemStatus;
                }
            }
            set
            {
                lock (m_lock)
                {
                    m_systemStatus = value;
                }
            }
        }

        /// <summary>
        /// Gets the data generator used for random value generation.
        /// </summary>
        public Opc.Ua.Test.DataGenerator Generator { get; }

        /// <summary>
        /// Creates an archive for the variable.
        /// </summary>
        public void EnableHistoryArchiving(BaseVariableState variable)
        {
            if (variable == null)
            {
                return;
            }

            if (variable.ValueRank == ValueRanks.Scalar)
            {
                m_historyArchive.CreateRecord(
                    variable.NodeId,
                    TypeInfo.GetBuiltInType(variable.DataType));
            }
        }

        /// <summary>
        /// Returns the history file for the variable.
        /// </summary>
        public IHistoryDataSource GetHistoryFile(BaseVariableState variable)
        {
            if (variable == null)
            {
                return null;
            }

            return m_historyArchive.GetHistoryFile(variable.NodeId);
        }

        /// <summary>
        /// Returns a new value for the variable.
        /// </summary>
        public Variant ReadValue(BaseVariableState variable)
        {
            lock (m_lock)
            {
                switch (variable.NumericId)
                {
                    case Variables.ScalarValueObjectType_BooleanValue:
                    case Variables.UserScalarValueObjectType_BooleanValue:
                        return Generator.GetRandomBoolean();
                    case Variables.ScalarValueObjectType_SByteValue:
                    case Variables.UserScalarValueObjectType_SByteValue:
                        return Generator.GetRandomSByte();
                    case Variables.AnalogScalarValueObjectType_SByteValue:
                        return (sbyte)(((int)(Generator.GetRandomUInt32() % 201)) - 100);
                    case Variables.ScalarValueObjectType_ByteValue:
                    case Variables.UserScalarValueObjectType_ByteValue:
                        return Generator.GetRandomByte();
                    case Variables.AnalogScalarValueObjectType_ByteValue:
                        return (byte)((Generator.GetRandomUInt32() % 201) + 50);
                    case Variables.ScalarValueObjectType_Int16Value:
                    case Variables.UserScalarValueObjectType_Int16Value:
                        return Generator.GetRandomInt16();
                    case Variables.AnalogScalarValueObjectType_Int16Value:
                        return (short)(((int)(Generator.GetRandomUInt32() % 201)) - 100);
                    case Variables.ScalarValueObjectType_UInt16Value:
                    case Variables.UserScalarValueObjectType_UInt16Value:
                        return Generator.GetRandomUInt16();
                    case Variables.AnalogScalarValueObjectType_UInt16Value:
                        return Variant.From((ushort)((Generator.GetRandomUInt32() % 201) + 50));
                    case Variables.ScalarValueObjectType_Int32Value:
                    case Variables.UserScalarValueObjectType_Int32Value:
                        return Generator.GetRandomInt32();
                    case Variables.AnalogScalarValueObjectType_Int32Value:
                    case Variables.AnalogScalarValueObjectType_IntegerValue:
                        return Variant.From(((int)(Generator.GetRandomUInt32() % 201)) - 100);
                    case Variables.ScalarValueObjectType_UInt32Value:
                    case Variables.UserScalarValueObjectType_UInt32Value:
                        return Generator.GetRandomUInt32();
                    case Variables.AnalogScalarValueObjectType_UInt32Value:
                    case Variables.AnalogScalarValueObjectType_UIntegerValue:
                        return Variant.From((Generator.GetRandomUInt32() % 201) + 50);
                    case Variables.ScalarValueObjectType_Int64Value:
                    case Variables.UserScalarValueObjectType_Int64Value:
                        return Generator.GetRandomInt64();
                    case Variables.AnalogScalarValueObjectType_Int64Value:
                        return Variant.From((long)(((int)(Generator.GetRandomUInt32() % 201)) - 100));
                    case Variables.ScalarValueObjectType_UInt64Value:
                    case Variables.UserScalarValueObjectType_UInt64Value:
                        return Generator.GetRandomUInt64();
                    case Variables.AnalogScalarValueObjectType_UInt64Value:
                        return Variant.From((ulong)((Generator.GetRandomUInt32() % 201) + 50));
                    case Variables.ScalarValueObjectType_FloatValue:
                    case Variables.UserScalarValueObjectType_FloatValue:
                        return Generator.GetRandomFloat();
                    case Variables.AnalogScalarValueObjectType_FloatValue:
                        return Variant.From((float)(((int)(Generator.GetRandomUInt32() % 201)) - 100));
                    case Variables.ScalarValueObjectType_DoubleValue:
                    case Variables.UserScalarValueObjectType_DoubleValue:
                        return Generator.GetRandomDouble();
                    case Variables.AnalogScalarValueObjectType_DoubleValue:
                    case Variables.AnalogScalarValueObjectType_NumberValue:
                        return Variant.From((double)(((int)(Generator.GetRandomUInt32() % 201)) - 100));
                    case Variables.ScalarValueObjectType_StringValue:
                    case Variables.UserScalarValueObjectType_StringValue:
                        return Generator.GetRandomString();
                    case Variables.ScalarValueObjectType_DateTimeValue:
                    case Variables.UserScalarValueObjectType_DateTimeValue:
                        return Generator.GetRandomDateTime();
                    case Variables.ScalarValueObjectType_GuidValue:
                    case Variables.UserScalarValueObjectType_GuidValue:
                        return Generator.GetRandomGuid();
                    case Variables.ScalarValueObjectType_ByteStringValue:
                    case Variables.UserScalarValueObjectType_ByteStringValue:
                        return Generator.GetRandomByteString();
                    case Variables.ScalarValueObjectType_XmlElementValue:
                    case Variables.UserScalarValueObjectType_XmlElementValue:
                        return Generator.GetRandomXmlElement();
                    case Variables.ScalarValueObjectType_NodeIdValue:
                    case Variables.UserScalarValueObjectType_NodeIdValue:
                        return Generator.GetRandomNodeId();
                    case Variables.ScalarValueObjectType_ExpandedNodeIdValue:
                    case Variables.UserScalarValueObjectType_ExpandedNodeIdValue:
                        return Generator.GetRandomExpandedNodeId();
                    case Variables.ScalarValueObjectType_QualifiedNameValue:
                    case Variables.UserScalarValueObjectType_QualifiedNameValue:
                        return Generator.GetRandomQualifiedName();
                    case Variables.ScalarValueObjectType_LocalizedTextValue:
                    case Variables.UserScalarValueObjectType_LocalizedTextValue:
                        return Generator.GetRandomLocalizedText();
                    case Variables.ScalarValueObjectType_StatusCodeValue:
                    case Variables.UserScalarValueObjectType_StatusCodeValue:
                        return Generator.GetRandomStatusCode();
                    case Variables.ScalarValueObjectType_VariantValue:
                    case Variables.UserScalarValueObjectType_VariantValue:
                        return Generator.GetRandomVariant();
                    case Variables.ScalarValueObjectType_StructureValue:
                        return GetRandomStructure();
                    case Variables.ScalarValueObjectType_EnumerationValue:
                        return Generator.GetRandomInt32();
                    case Variables.ScalarValueObjectType_NumberValue:
                        return Generator.GetRandomScalar(BuiltInType.Number);
                    case Variables.ScalarValueObjectType_IntegerValue:
                        return Generator.GetRandomScalar(BuiltInType.Integer);
                    case Variables.ScalarValueObjectType_UIntegerValue:
                        return Generator.GetRandomScalar(BuiltInType.UInteger);
                    case Variables.Data_Static_Structure_VectorStructure:
                    case Variables.Data_Dynamic_Structure_VectorStructure:
                    case Variables.StructureValueObjectType_VectorStructure:
                    case Variables.ScalarValueObjectType_VectorValue:
                        return Variant.FromStructure(GetRandomVector());
                    case Variables.ArrayValueObjectType_VectorValue:
                        return Variant.FromStructure(GetRandomArray(GetRandomVector));
                    // VectorUnion - Scalar
                    case Variables.ScalarValueObjectType_VectorUnionValue:
                        return Variant.FromStructure(GetRandomVectorUnion());
                    // VectorUnion - Array
                    case Variables.ArrayValueObjectType_VectorUnionValue:
                        return Variant.FromStructure(GetRandomArray(GetRandomVectorUnion));
                    // VectorWithOptionalFields - Scalar
                    case Variables.ScalarValueObjectType_VectorWithOptionalFieldsValue:
                        return Variant.FromStructure(GetRandomVectorWithOptionalFields());
                    // VectorWithOptionalFields - Array
                    case Variables.ArrayValueObjectType_VectorWithOptionalFieldsValue:
                        return Variant.FromStructure(GetRandomArray(GetRandomVectorWithOptionalFields));
                    // MultipleVectors - Scalar
                    case Variables.ScalarValueObjectType_MultipleVectorsValue:
                        return Variant.FromStructure(GetRandomMultipleVectors());
                    // MultipleVectors - Array
                    case Variables.ArrayValueObjectType_MultipleVectorsValue:
                        return Variant.FromStructure(GetRandomArray(GetRandomMultipleVectors));
                    case Variables.ArrayValueObjectType_BooleanValue:
                    case Variables.UserArrayValueObjectType_BooleanValue:
                        return Generator.GetRandomBooleanArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_SByteValue:
                    case Variables.UserArrayValueObjectType_SByteValue:
                        return Generator.GetRandomSByteArray(false, 100, false).ToArrayOf();
                    case Variables.AnalogArrayValueObjectType_SByteValue:
                    {
                        sbyte[] values = Generator.GetRandomSByteArray(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (sbyte)(((int)(Generator.GetRandomUInt32() % 201)) - 100);
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_ByteValue:
                    case Variables.UserArrayValueObjectType_ByteValue:
                        return Generator.GetRandomByteArray(false, 100, false).ToArrayOf();
                    case Variables.AnalogArrayValueObjectType_ByteValue:
                    {
                        byte[] values = Generator.GetRandomByteArray(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (byte)((Generator.GetRandomUInt32() % 201) + 50);
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_Int16Value:
                    case Variables.UserArrayValueObjectType_Int16Value:
                        return Generator.GetRandomInt16Array(false, 100, false).ToArrayOf();
                    case Variables.AnalogArrayValueObjectType_Int16Value:
                    {
                        short[] values = Generator.GetRandomInt16Array(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (short)(((int)(Generator.GetRandomUInt32() % 201)) - 100);
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_UInt16Value:
                    case Variables.UserArrayValueObjectType_UInt16Value:
                        return Generator.GetRandomUInt16Array(false, 100, false).ToArrayOf();
                    case Variables.AnalogArrayValueObjectType_UInt16Value:
                    {
                        ushort[] values = Generator.GetRandomUInt16Array(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (ushort)((Generator.GetRandomUInt32() % 201) + 50);
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_Int32Value:
                    case Variables.UserArrayValueObjectType_Int32Value:
                        return Generator.GetRandomInt32Array(false, 100, false).ToArrayOf();
                    case Variables.AnalogArrayValueObjectType_Int32Value:
                    case Variables.AnalogArrayValueObjectType_IntegerValue:
                    {
                        int[] values = Generator.GetRandomInt32Array(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = ((int)(Generator.GetRandomUInt32() % 201)) - 100;
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_UInt32Value:
                    case Variables.UserArrayValueObjectType_UInt32Value:
                        return Generator.GetRandomUInt32Array(false, 100, false).ToArrayOf();
                    case Variables.AnalogArrayValueObjectType_UInt32Value:
                    case Variables.AnalogArrayValueObjectType_UIntegerValue:
                    {
                        uint[] values = Generator.GetRandomUInt32Array(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (Generator.GetRandomUInt32() % 201) + 50;
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_Int64Value:
                    case Variables.UserArrayValueObjectType_Int64Value:
                        return Generator.GetRandomInt64Array(false, 100, false).ToArrayOf();
                    case Variables.AnalogArrayValueObjectType_Int64Value:
                    {
                        long[] values = Generator.GetRandomInt64Array(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = ((int)(Generator.GetRandomUInt32() % 201)) - 100;
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_UInt64Value:
                    case Variables.UserArrayValueObjectType_UInt64Value:
                        return Generator.GetRandomUInt64Array(false, 100, false).ToArrayOf();
                    case Variables.AnalogArrayValueObjectType_UInt64Value:
                    {
                        ulong[] values = Generator.GetRandomUInt64Array(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (Generator.GetRandomUInt32() % 201) + 50;
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_FloatValue:
                    case Variables.UserArrayValueObjectType_FloatValue:
                        return Generator.GetRandomFloatArray(false, 100, false).ToArrayOf();
                    case Variables.AnalogArrayValueObjectType_FloatValue:
                    {
                        float[] values = Generator.GetRandomFloatArray(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = ((int)(Generator.GetRandomUInt32() % 201)) - 100;
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_DoubleValue:
                    case Variables.UserArrayValueObjectType_DoubleValue:
                        return Generator.GetRandomDoubleArray(false, 100, false).ToArrayOf();
                    case Variables.AnalogArrayValueObjectType_DoubleValue:
                    case Variables.AnalogArrayValueObjectType_NumberValue:
                    {
                        double[] values = Generator.GetRandomDoubleArray(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = ((int)(Generator.GetRandomUInt32() % 201)) - 100;
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_StringValue:
                    case Variables.UserArrayValueObjectType_StringValue:
                        return Generator.GetRandomStringArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_DateTimeValue:
                    case Variables.UserArrayValueObjectType_DateTimeValue:
                        return Generator.GetRandomDateTimeArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_GuidValue:
                    case Variables.UserArrayValueObjectType_GuidValue:
                        return Generator.GetRandomGuidArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_ByteStringValue:
                    case Variables.UserArrayValueObjectType_ByteStringValue:
                        return Generator.GetRandomByteStringArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_XmlElementValue:
                    case Variables.UserArrayValueObjectType_XmlElementValue:
                        return Generator.GetRandomXmlElementArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_NodeIdValue:
                    case Variables.UserArrayValueObjectType_NodeIdValue:
                        return Generator.GetRandomNodeIdArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_ExpandedNodeIdValue:
                    case Variables.UserArrayValueObjectType_ExpandedNodeIdValue:
                        return Generator.GetRandomExpandedNodeIdArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_QualifiedNameValue:
                    case Variables.UserArrayValueObjectType_QualifiedNameValue:
                        return Generator.GetRandomQualifiedNameArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_LocalizedTextValue:
                    case Variables.UserArrayValueObjectType_LocalizedTextValue:
                        return Generator.GetRandomLocalizedTextArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_StatusCodeValue:
                    case Variables.UserArrayValueObjectType_StatusCodeValue:
                        return Generator.GetRandomStatusCodeArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_VariantValue:
                    case Variables.UserArrayValueObjectType_VariantValue:
                        return Generator.GetRandomVariantArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_StructureValue:
                    {
                        ExtensionObject[] values = Generator.GetRandomExtensionObjectArray(
                            false,
                            10,
                            false);

                        for (int ii = 0; values != null && ii < values.Length; ii++)
                        {
                            values[ii] = GetRandomStructure();
                        }

                        return values.ToArrayOf();
                    }
                    case Variables.ArrayValueObjectType_EnumerationValue:
                        return Generator.GetRandomInt32Array(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_NumberValue:
                        return Generator.GetRandomNumberArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_IntegerValue:
                        return Generator.GetRandomIntegerArray(false, 100, false).ToArrayOf();
                    case Variables.ArrayValueObjectType_UIntegerValue:
                        return Generator.GetRandomUIntegerArray(false, 100, false).ToArrayOf();
                    case Variables.Data_Static_Structure_ScalarStructure:
                    case Variables.Data_Dynamic_Structure_ScalarStructure:
                    case Variables.StructureValueObjectType_ScalarStructure:
                        return Variant.FromStructure(GetRandomScalarStructureDataType());
                    case Variables.Data_Static_Structure_ScalarStructure_BooleanValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_BooleanValue:
                        return Generator.GetRandomBoolean();
                    case Variables.Data_Static_Structure_ScalarStructure_SByteValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_SByteValue:
                        return Generator.GetRandomSByte();
                    case Variables.Data_Static_Structure_ScalarStructure_ByteValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_ByteValue:
                        return Generator.GetRandomByte();
                    case Variables.Data_Static_Structure_ScalarStructure_Int16Value:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_Int16Value:
                        return Generator.GetRandomInt16();
                    case Variables.Data_Static_Structure_ScalarStructure_UInt16Value:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_UInt16Value:
                        return Generator.GetRandomUInt16();
                    case Variables.Data_Static_Structure_ScalarStructure_Int32Value:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_Int32Value:
                        return Generator.GetRandomInt32();
                    case Variables.Data_Static_Structure_ScalarStructure_UInt32Value:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_UInt32Value:
                        return Generator.GetRandomUInt32();
                    case Variables.Data_Static_Structure_ScalarStructure_Int64Value:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_Int64Value:
                        return Generator.GetRandomInt64();
                    case Variables.Data_Static_Structure_ScalarStructure_UInt64Value:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_UInt64Value:
                        return Generator.GetRandomUInt64();
                    case Variables.Data_Static_Structure_ScalarStructure_FloatValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_FloatValue:
                        return Generator.GetRandomFloat();
                    case Variables.Data_Static_Structure_ScalarStructure_DoubleValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_DoubleValue:
                        return Generator.GetRandomDouble();
                    case Variables.Data_Static_Structure_ScalarStructure_StringValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_StringValue:
                        return Generator.GetRandomString();
                    case Variables.Data_Static_Structure_ScalarStructure_DateTimeValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_DateTimeValue:
                        return Generator.GetRandomDateTime();
                    case Variables.Data_Static_Structure_ScalarStructure_GuidValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_GuidValue:
                        return Generator.GetRandomGuid();
                    case Variables.Data_Static_Structure_ScalarStructure_ByteStringValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_ByteStringValue:
                        return Generator.GetRandomByteString();
                    case Variables.Data_Static_Structure_ScalarStructure_XmlElementValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_XmlElementValue:
                        return Generator.GetRandomXmlElement();
                    case Variables.Data_Static_Structure_ScalarStructure_NodeIdValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_NodeIdValue:
                        return Generator.GetRandomNodeId();
                    case Variables.Data_Static_Structure_ScalarStructure_ExpandedNodeIdValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_ExpandedNodeIdValue:
                        return Generator.GetRandomExpandedNodeId();
                    case Variables.Data_Static_Structure_ScalarStructure_QualifiedNameValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_QualifiedNameValue:
                        return Generator.GetRandomQualifiedName();
                    case Variables.Data_Static_Structure_ScalarStructure_LocalizedTextValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_LocalizedTextValue:
                        return Generator.GetRandomLocalizedText();
                    case Variables.Data_Static_Structure_ScalarStructure_StatusCodeValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_StatusCodeValue:
                        return Generator.GetRandomStatusCode();
                    case Variables.Data_Static_Structure_ScalarStructure_VariantValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_VariantValue:
                        return Generator.GetRandomVariant();
                    case Variables.Data_Static_Structure_ScalarStructure_EnumerationValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_EnumerationValue:
                        return Generator.GetRandomByte();
                    case Variables.Data_Static_Structure_ScalarStructure_StructureValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_StructureValue:
                        return GetRandomStructure();
                    case Variables.Data_Static_Structure_ScalarStructure_NumberValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_NumberValue:
                        return Generator.GetRandomNumber();
                    case Variables.Data_Static_Structure_ScalarStructure_IntegerValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_IntegerValue:
                        return Generator.GetRandomInteger();
                    case Variables.Data_Static_Structure_ScalarStructure_UIntegerValue:
                    case Variables.Data_Dynamic_Structure_ScalarStructure_UIntegerValue:
                        return Generator.GetRandomUInteger();
                    default:
                        return Variant.Null;
                }
            }
        }

        /// <summary>
        /// Gets a random Array (one to eight elements).
        /// </summary>
        /// <typeparam name="T">The type of the elements</typeparam>
        /// <param name="methodForSingleObject">Method, to create a single element</param>
        private ArrayOf<T> GetRandomArray<T>(Func<T> methodForSingleObject)
        {
            int size = (Generator.GetRandomByte() % 8) + 1;
            var result = new T[size];
            for (int ii = 0; ii < size; ii++)
            {
                result[ii] = methodForSingleObject();
            }
            return result;
        }

        /// <summary>
        /// Return random vector.
        /// </summary>
        public Vector GetRandomVector()
        {
            return new Vector
            {
                X = Generator.GetRandomDouble(),
                Y = Generator.GetRandomDouble(),
                Z = Generator.GetRandomDouble()
            };
        }

        public VectorUnion GetRandomVectorUnion()
        {
            return new VectorUnion
            {
                SwitchField = (VectorUnionFields)(Generator.GetRandomUInt16() % 4),
                X = Generator.GetRandomDouble(),
                Y = Generator.GetRandomDouble(),
                Z = Generator.GetRandomDouble()
            };
        }

        public VectorWithOptionalFields GetRandomVectorWithOptionalFields()
        {
            VectorWithOptionalFieldsFields encodingMask = VectorWithOptionalFieldsFields.None;
            if (Generator.GetRandomBoolean())
            {
                encodingMask |= VectorWithOptionalFieldsFields.X;
            }

            if (Generator.GetRandomBoolean())
            {
                encodingMask |= VectorWithOptionalFieldsFields.Y;
            }

            if (Generator.GetRandomBoolean())
            {
                encodingMask |= VectorWithOptionalFieldsFields.Z;
            }

            return new VectorWithOptionalFields
            {
                EncodingMask = (uint)encodingMask,
                X = Generator.GetRandomDouble(),
                Y = Generator.GetRandomDouble(),
                Z = Generator.GetRandomDouble()
            };
        }

        public MultipleVectors GetRandomMultipleVectors()
        {
            return new MultipleVectors
            {
                Vector = GetRandomVector(),
                VectorUnion = GetRandomVectorUnion(),
                VectorWithOptionalFields = GetRandomVectorWithOptionalFields(),
                VectorArray = GetRandomArray(GetRandomVector),
                VectorUnionArray = GetRandomArray(GetRandomVectorUnion),
                VectorWithOptionalFieldsArray = GetRandomArray(GetRandomVectorWithOptionalFields)
            };
        }

        /// <summary>
        /// Returns a random structure.
        /// </summary>
        private ExtensionObject GetRandomStructure()
        {
            if (Generator.GetRandomBoolean())
            {
                ScalarStructureDataType value = GetRandomScalarStructureDataType();
                return new ExtensionObject(value);
            }
            else
            {
                ArrayValueDataType value = GetRandomArrayValueDataType();
                return new ExtensionObject(value);
            }
        }

        public ScalarStructureDataType GetRandomScalarStructureDataType()
        {
            return new ScalarStructureDataType
            {
                BooleanValue = Generator.GetRandomBoolean(),
                SByteValue = Generator.GetRandomSByte(),
                ByteValue = Generator.GetRandomByte(),
                Int16Value = Generator.GetRandomInt16(),
                UInt16Value = Generator.GetRandomUInt16(),
                Int32Value = Generator.GetRandomInt32(),
                UInt32Value = Generator.GetRandomUInt32(),
                Int64Value = Generator.GetRandomInt64(),
                UInt64Value = Generator.GetRandomUInt64(),
                FloatValue = Generator.GetRandomFloat(),
                DoubleValue = Generator.GetRandomDouble(),
                StringValue = Generator.GetRandomString(),
                DateTimeValue = Generator.GetRandomDateTime(),
                GuidValue = Generator.GetRandomGuid(),
                ByteStringValue = Generator.GetRandomByteString(),
                XmlElementValue = Generator.GetRandomXmlElement(),
                NodeIdValue = Generator.GetRandomNodeId(),
                ExpandedNodeIdValue = Generator.GetRandomExpandedNodeId(),
                QualifiedNameValue = Generator.GetRandomQualifiedName(),
                LocalizedTextValue = Generator.GetRandomLocalizedText(),
                StatusCodeValue = Generator.GetRandomStatusCode(),
                VariantValue = Generator.GetRandomVariant(),
                IntegerValue = Generator.GetRandomInteger(),
                UIntegerValue = Generator.GetRandomUInteger(),
                NumberValue = Generator.GetRandomNumber()
            };
        }

        public ArrayValueDataType GetRandomArrayValueDataType()
        {
            return new ArrayValueDataType
            {
                BooleanValue = Generator.GetRandomBooleanArray(false, 10, false),
                SByteValue = Generator.GetRandomSByteArray(false, 10, false),
                ByteValue = Generator.GetRandomByteArray(false, 10, false),
                Int16Value = Generator.GetRandomInt16Array(false, 10, false),
                UInt16Value = Generator.GetRandomUInt16Array(false, 10, false),
                Int32Value = Generator.GetRandomInt32Array(false, 10, false),
                UInt32Value = Generator.GetRandomUInt32Array(false, 10, false),
                Int64Value = Generator.GetRandomInt64Array(false, 10, false),
                UInt64Value = Generator.GetRandomUInt64Array(false, 10, false),
                FloatValue = Generator.GetRandomFloatArray(false, 10, false),
                DoubleValue = Generator.GetRandomDoubleArray(false, 10, false),
                StringValue = Generator.GetRandomStringArray(false, 10, false),
                DateTimeValue = Generator.GetRandomDateTimeArray(false, 10, false),
                GuidValue = Generator.GetRandomGuidArray(false, 10, false),
                ByteStringValue = Generator.GetRandomByteStringArray(false, 10, false),
                XmlElementValue = Generator.GetRandomXmlElementArray(false, 10, false),
                NodeIdValue = Generator.GetRandomNodeIdArray(false, 10, false),
                ExpandedNodeIdValue = Generator.GetRandomExpandedNodeIdArray(false, 10, false),
                QualifiedNameValue = Generator.GetRandomQualifiedNameArray(false, 10, false),
                LocalizedTextValue = Generator.GetRandomLocalizedTextArray(false, 10, false),
                StatusCodeValue = Generator.GetRandomStatusCodeArray(false, 10, false),
                VariantValue = Generator.GetRandomVariantArray(false, 10, false)
            };
        }

        public void StartMonitoringValue(
            uint monitoredItemId,
            double samplingInterval,
            BaseVariableState variable)
        {
            lock (m_lock)
            {
                m_monitoredNodes ??= [];

                m_monitoredNodes[monitoredItemId] = variable;
                m_samplingNodes = null;

                SetSamplingInterval(samplingInterval);
            }
        }

        public void SetSamplingInterval(double samplingInterval)
        {
            lock (m_lock)
            {
                if (samplingInterval < 0)
                {
                    // m_samplingEvent.Set();
                    m_minimumSamplingInterval = int.MaxValue;

                    m_timer?.Dispose();
                    m_timer = null;

                    return;
                }

                if (m_minimumSamplingInterval > samplingInterval)
                {
                    m_minimumSamplingInterval = (int)samplingInterval;

                    if (m_minimumSamplingInterval < 100)
                    {
                        m_minimumSamplingInterval = 100;
                    }

                    m_timer?.Dispose();
                    m_timer = null;

                    m_timer = new Timer(
                        DoSample,
                        null,
                        m_minimumSamplingInterval,
                        m_minimumSamplingInterval);
                }
            }
        }

        private void DoSample(object state)
        {
            m_logger.LogTrace(
                "DoSample HiRes={HiRes:ss.ffff} Now={CurrentTime:ss.ffff}",
                HiResClock.UtcNow,
                DateTime.UtcNow);

            var samples = new Queue<Sample>();
            var generateValues = new List<BaseVariableState>();

            lock (m_lock)
            {
                if (m_monitoredNodes == null)
                {
                    return;
                }

                m_samplingNodes ??=
                [
                    .. m_monitoredNodes.Values.Distinct(new NodeStateComparer())
                        .Cast<BaseVariableState>()
                ];

                foreach (BaseVariableState variable in m_samplingNodes)
                {
                    if (variable is ITestDataSystemValuesGenerator)
                    {
                        generateValues.Add(variable);
                    }
                    else if (variable.Parent is ITestDataSystemValuesGenerator)
                    {
                        generateValues.Add(variable.Parent as BaseVariableState);
                    }
                    else
                    {
                        Variant value = ReadValue(variable);
                        if (!value.IsNull)
                        {
                            var sample = new Sample
                            {
                                Variable = variable,
                                Value = value,
                                StatusCode = StatusCodes.Good,
                                Timestamp = DateTime.UtcNow
                            };
                            samples.Enqueue(sample);
                        }
                    }
                }
            }

            while (samples.Count > 0)
            {
                Sample sample = samples.Dequeue();

                m_callback.OnDataChange(
                    sample.Variable,
                    sample.Value,
                    sample.StatusCode,
                    sample.Timestamp);
            }

            foreach (BaseVariableState generateValue in generateValues)
            {
                m_callback.OnGenerateValues(generateValue);
            }
        }

        public void StopMonitoringValue(uint monitoredItemId)
        {
            lock (m_lock)
            {
                if (m_monitoredNodes == null)
                {
                    return;
                }

                m_monitoredNodes.Remove(monitoredItemId);
                m_samplingNodes = null;

                if (m_monitoredNodes.Count == 0)
                {
                    SetSamplingInterval(-1);
                }
            }
        }

        private sealed class Sample
        {
            public BaseVariableState Variable;
            public Variant Value;
            public StatusCode StatusCode;
            public DateTime Timestamp;
        }

        private readonly Lock m_lock = new();
        private readonly ITestDataSystemCallback m_callback;
        private readonly ILogger m_logger;
        private int m_minimumSamplingInterval;
        private Dictionary<uint, BaseVariableState> m_monitoredNodes;
        private IList<BaseVariableState> m_samplingNodes;
        private Timer m_timer;
        private StatusCode m_systemStatus;
        private readonly HistoryArchive m_historyArchive;
    }
}
