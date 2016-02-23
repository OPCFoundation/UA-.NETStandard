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
using System.Threading;
using System.Xml;
using System.IO;
using Opc.Ua;
using Opc.Ua.Server;

namespace TestData
{   
    public interface ITestDataSystemCallback
    {
        void OnDataChange(
            BaseVariableState variable,
            object value,
            StatusCode statusCode,
            DateTime timestamp);
    }
    
    public class TestDataSystem
    {
        public TestDataSystem(ITestDataSystemCallback callback, NamespaceTable namespaceUris, StringTable serverUris)
        {
            m_callback = callback;
            m_minimumSamplingInterval = Int32.MaxValue;
            m_monitoredNodes = new Dictionary<uint,BaseVariableState>();
            m_generator = new Opc.Ua.Test.DataGenerator(null);
            m_generator.NamespaceUris = namespaceUris;
            m_generator.ServerUris = serverUris;
            m_historyArchive = new HistoryArchive();
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
                m_historyArchive.CreateRecord(variable.NodeId, TypeInfo.GetBuiltInType(variable.DataType));
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
        public object ReadValue(BaseVariableState variable)
        {
            lock (m_lock)
            {
                switch (variable.NumericId)
                {
                    case TestData.Variables.ScalarValueObjectType_BooleanValue:
                    case TestData.Variables.UserScalarValueObjectType_BooleanValue:
                    {
                        return m_generator.GetRandom<bool>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_SByteValue:
                    case TestData.Variables.UserScalarValueObjectType_SByteValue:
                    {
                        return m_generator.GetRandom<sbyte>(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_SByteValue:
                    {
                        return (sbyte)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_ByteValue:
                    case TestData.Variables.UserScalarValueObjectType_ByteValue:
                    {
                        return m_generator.GetRandom<byte>(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_ByteValue:
                    {
                        return (byte)((m_generator.GetRandom<uint>(false)%201) + 50);
                    }

                    case TestData.Variables.ScalarValueObjectType_Int16Value:
                    case TestData.Variables.UserScalarValueObjectType_Int16Value:
                    {
                        return m_generator.GetRandom<short>(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_Int16Value:
                    {
                        return (short)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_UInt16Value:
                    case TestData.Variables.UserScalarValueObjectType_UInt16Value:
                    {
                        return m_generator.GetRandom<ushort>(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_UInt16Value:
                    {
                        return (ushort)((m_generator.GetRandom<uint>(false)%201) + 50);
                    }

                    case TestData.Variables.ScalarValueObjectType_Int32Value:
                    case TestData.Variables.UserScalarValueObjectType_Int32Value:
                    {
                        return m_generator.GetRandom<int>(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_Int32Value:
                    case TestData.Variables.AnalogScalarValueObjectType_IntegerValue:
                    {
                        return (int)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_UInt32Value:
                    case TestData.Variables.UserScalarValueObjectType_UInt32Value:
                    {
                        return m_generator.GetRandom<uint>(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_UInt32Value:
                    case TestData.Variables.AnalogScalarValueObjectType_UIntegerValue:
                    {
                        return (uint)((m_generator.GetRandom<uint>(false)%201) + 50);
                    }

                    case TestData.Variables.ScalarValueObjectType_Int64Value:
                    case TestData.Variables.UserScalarValueObjectType_Int64Value:
                    {
                        return m_generator.GetRandom<long>(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_Int64Value:
                    {
                        return (long)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_UInt64Value:
                    case TestData.Variables.UserScalarValueObjectType_UInt64Value:
                    {
                        return m_generator.GetRandom<ulong>(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_UInt64Value:
                    {
                        return (ulong)((m_generator.GetRandom<uint>(false)%201) + 50);
                    }

                    case TestData.Variables.ScalarValueObjectType_FloatValue:
                    case TestData.Variables.UserScalarValueObjectType_FloatValue:
                    {
                        return m_generator.GetRandom<float>(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_FloatValue:
                    {
                        return (float)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_DoubleValue:
                    case TestData.Variables.UserScalarValueObjectType_DoubleValue:
                    {
                        return m_generator.GetRandom<double>(false);
                    }

                    case TestData.Variables.AnalogScalarValueObjectType_DoubleValue:
                    case TestData.Variables.AnalogScalarValueObjectType_NumberValue:
                    {
                        return (double)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                    }

                    case TestData.Variables.ScalarValueObjectType_StringValue:
                    case TestData.Variables.UserScalarValueObjectType_StringValue:
                    {
                        return m_generator.GetRandom<string>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_DateTimeValue:
                    case TestData.Variables.UserScalarValueObjectType_DateTimeValue:
                    {
                        return m_generator.GetRandom<DateTime>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_GuidValue:
                    case TestData.Variables.UserScalarValueObjectType_GuidValue:
                    {
                        return m_generator.GetRandom<Guid>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_ByteStringValue:
                    case TestData.Variables.UserScalarValueObjectType_ByteStringValue:
                    {
                        return m_generator.GetRandom<byte[]>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_XmlElementValue:
                    case TestData.Variables.UserScalarValueObjectType_XmlElementValue:
                    {
                        return m_generator.GetRandom<XmlElement>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_NodeIdValue:
                    case TestData.Variables.UserScalarValueObjectType_NodeIdValue:
                    {
                        return m_generator.GetRandom<Opc.Ua.NodeId>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_ExpandedNodeIdValue:
                    case TestData.Variables.UserScalarValueObjectType_ExpandedNodeIdValue:
                    {
                        return m_generator.GetRandom<ExpandedNodeId>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_QualifiedNameValue:
                    case TestData.Variables.UserScalarValueObjectType_QualifiedNameValue:
                    {
                        return m_generator.GetRandom<QualifiedName>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_LocalizedTextValue:
                    case TestData.Variables.UserScalarValueObjectType_LocalizedTextValue:
                    {
                        return m_generator.GetRandom<LocalizedText>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_StatusCodeValue:
                    case TestData.Variables.UserScalarValueObjectType_StatusCodeValue:
                    {
                        return m_generator.GetRandom<StatusCode>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_VariantValue:
                    case TestData.Variables.UserScalarValueObjectType_VariantValue:
                    {
                        return m_generator.GetRandomVariant(false).Value;
                    }

                    case TestData.Variables.ScalarValueObjectType_StructureValue:
                    {
                        return GetRandomStructure();
                    }

                    case TestData.Variables.ScalarValueObjectType_EnumerationValue:
                    {
                        return m_generator.GetRandom<int>(false);
                    }

                    case TestData.Variables.ScalarValueObjectType_NumberValue:
                    {
                        return m_generator.GetRandom(BuiltInType.Number);
                    }

                    case TestData.Variables.ScalarValueObjectType_IntegerValue:
                    {
                        return m_generator.GetRandom(BuiltInType.Integer);
                    }

                    case TestData.Variables.ScalarValueObjectType_UIntegerValue:
                    {
                        return m_generator.GetRandom(BuiltInType.UInteger);
                    }

                    case TestData.Variables.ArrayValueObjectType_BooleanValue:
                    case TestData.Variables.UserArrayValueObjectType_BooleanValue:
                    {
                        return m_generator.GetRandomArray<bool>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_SByteValue:
                    case TestData.Variables.UserArrayValueObjectType_SByteValue:
                    {
                        return m_generator.GetRandomArray<sbyte>(false, 100, false);
                    }                        

                    case TestData.Variables.AnalogArrayValueObjectType_SByteValue:
                    {
                        sbyte[] values = m_generator.GetRandomArray<sbyte>(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (sbyte)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                        }

                        return values;
                    }
                        
                    case TestData.Variables.ArrayValueObjectType_ByteValue:
                    case TestData.Variables.UserArrayValueObjectType_ByteValue:
                    {
                        return m_generator.GetRandomArray<byte>(false, 100, false);
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_ByteValue:
                    {
                        byte[] values = m_generator.GetRandomArray<byte>(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (byte)((m_generator.GetRandom<uint>(false)%201) + 50);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_Int16Value:
                    case TestData.Variables.UserArrayValueObjectType_Int16Value:
                    {
                        return m_generator.GetRandomArray<short>(false, 100, false);
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_Int16Value:
                    {
                        short[] values = m_generator.GetRandomArray<short>(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (short)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_UInt16Value:
                    case TestData.Variables.UserArrayValueObjectType_UInt16Value:
                    {
                        return m_generator.GetRandomArray<ushort>(false, 100, false);
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_UInt16Value:
                    {
                        ushort[] values = m_generator.GetRandomArray<ushort>(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (ushort)((m_generator.GetRandom<uint>(false)%201) + 50);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_Int32Value:
                    case TestData.Variables.UserArrayValueObjectType_Int32Value:
                    {
                        return m_generator.GetRandomArray<int>(false, 100, false);
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_Int32Value:
                    case TestData.Variables.AnalogArrayValueObjectType_IntegerValue:
                    {
                        int[] values = m_generator.GetRandomArray<int>(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (int)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_UInt32Value:
                    case TestData.Variables.UserArrayValueObjectType_UInt32Value:
                    {
                        return m_generator.GetRandomArray<uint>(false, 100, false);
                    }
                        
                    case TestData.Variables.AnalogArrayValueObjectType_UInt32Value:
                    case TestData.Variables.AnalogArrayValueObjectType_UIntegerValue:
                    {
                        uint[] values = m_generator.GetRandomArray<uint>(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (uint)((m_generator.GetRandom<uint>(false)%201) + 50);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_Int64Value:
                    case TestData.Variables.UserArrayValueObjectType_Int64Value:
                    {
                        return m_generator.GetRandomArray<long>(false, 100, false);
                    }
                        
                    case TestData.Variables.AnalogArrayValueObjectType_Int64Value:
                    {
                        long[] values = m_generator.GetRandomArray<long>(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (long)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_UInt64Value:
                    case TestData.Variables.UserArrayValueObjectType_UInt64Value:
                    {
                        return m_generator.GetRandomArray<ulong>(false, 100, false);
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_UInt64Value:
                    {
                        ulong[] values = m_generator.GetRandomArray<ulong>(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (ulong)((m_generator.GetRandom<uint>(false)%201) + 50);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_FloatValue:
                    case TestData.Variables.UserArrayValueObjectType_FloatValue:
                    {
                        return m_generator.GetRandomArray<float>(false, 100, false);
                    }

                    case TestData.Variables.AnalogArrayValueObjectType_FloatValue:
                    {
                        float[] values = m_generator.GetRandomArray<float>(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (float)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                        }

                        return values;
                    }
                        
                    case TestData.Variables.ArrayValueObjectType_DoubleValue:
                    case TestData.Variables.UserArrayValueObjectType_DoubleValue:
                    {
                        return m_generator.GetRandomArray<double>(false, 100, false);
                    }
                        
                    case TestData.Variables.AnalogArrayValueObjectType_DoubleValue:
                    case TestData.Variables.AnalogArrayValueObjectType_NumberValue:
                    {
                        double[] values = m_generator.GetRandomArray<double>(false, 100, false);

                        for (int ii = 0; ii < values.Length; ii++)
                        {
                            values[ii] = (double)(((int)(m_generator.GetRandom<uint>(false)%201)) - 100);
                        }

                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_StringValue:
                    case TestData.Variables.UserArrayValueObjectType_StringValue:
                    {
                        return m_generator.GetRandomArray<string>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_DateTimeValue:
                    case TestData.Variables.UserArrayValueObjectType_DateTimeValue:
                    {
                        return m_generator.GetRandomArray<DateTime>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_GuidValue:
                    case TestData.Variables.UserArrayValueObjectType_GuidValue:
                    {
                        return m_generator.GetRandomArray<Guid>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_ByteStringValue:
                    case TestData.Variables.UserArrayValueObjectType_ByteStringValue:
                    {
                        return m_generator.GetRandomArray<byte[]>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_XmlElementValue:
                    case TestData.Variables.UserArrayValueObjectType_XmlElementValue:
                    {
                        return m_generator.GetRandomArray<XmlElement>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_NodeIdValue:
                    case TestData.Variables.UserArrayValueObjectType_NodeIdValue:
                    {
                        return m_generator.GetRandomArray<Opc.Ua.NodeId>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_ExpandedNodeIdValue:
                    case TestData.Variables.UserArrayValueObjectType_ExpandedNodeIdValue:
                    {
                        return m_generator.GetRandomArray<ExpandedNodeId>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_QualifiedNameValue:
                    case TestData.Variables.UserArrayValueObjectType_QualifiedNameValue:
                    {
                        return m_generator.GetRandomArray<QualifiedName>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_LocalizedTextValue:
                    case TestData.Variables.UserArrayValueObjectType_LocalizedTextValue:
                    {
                        return m_generator.GetRandomArray<LocalizedText>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_StatusCodeValue:
                    case TestData.Variables.UserArrayValueObjectType_StatusCodeValue:
                    {
                        return m_generator.GetRandomArray<StatusCode>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_VariantValue:
                    case TestData.Variables.UserArrayValueObjectType_VariantValue:
                    {
                        return m_generator.GetRandomArray<object>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_StructureValue:
                    {
                        ExtensionObject[] values = m_generator.GetRandomArray<ExtensionObject>(false, 10, false);

                        for (int ii = 0; values != null && ii < values.Length; ii++)
                        {
                            values[ii] = GetRandomStructure();
                        }
                        
                        return values;
                    }

                    case TestData.Variables.ArrayValueObjectType_EnumerationValue:
                    {
                        return m_generator.GetRandomArray<int>(false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_NumberValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Number, false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_IntegerValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.Integer, false, 100, false);
                    }

                    case TestData.Variables.ArrayValueObjectType_UIntegerValue:
                    {
                        return m_generator.GetRandomArray(BuiltInType.UInteger, false, 100, false);
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Returns a random structure.
        /// </summary>
        private ExtensionObject GetRandomStructure()
        {
            if (m_generator.GetRandomBoolean())
            {
                ScalarValueDataType value = new ScalarValueDataType();
                
                value.BooleanValue = m_generator.GetRandom<bool>(false);
                value.SByteValue = m_generator.GetRandom<sbyte>(false);
                value.ByteValue = m_generator.GetRandom<byte>(false);
                value.Int16Value = m_generator.GetRandom<short>(false);
                value.UInt16Value = m_generator.GetRandom<ushort>(false);
                value.Int32Value = m_generator.GetRandom<int>(false);
                value.UInt32Value = m_generator.GetRandom<uint>(false);
                value.Int64Value = m_generator.GetRandom<long>(false);
                value.UInt64Value = m_generator.GetRandom<ulong>(false);
                value.FloatValue = m_generator.GetRandom<float>(false);
                value.DoubleValue = m_generator.GetRandom<double>(false);
                value.StringValue = m_generator.GetRandom<string>(false);
                value.DateTimeValue = m_generator.GetRandom<DateTime>(false);
                value.GuidValue = m_generator.GetRandom<Uuid>(false);
                value.ByteStringValue = m_generator.GetRandom<byte[]>(false);
                value.XmlElementValue = m_generator.GetRandom<XmlElement>(false);
                value.NodeIdValue = m_generator.GetRandom<Opc.Ua.NodeId>(false);
                value.ExpandedNodeIdValue = m_generator.GetRandom<ExpandedNodeId>(false);
                value.QualifiedNameValue = m_generator.GetRandom<QualifiedName>(false);
                value.LocalizedTextValue = m_generator.GetRandom<LocalizedText>(false);
                value.StatusCodeValue = m_generator.GetRandom<StatusCode>(false);
                value.VariantValue = m_generator.GetRandomVariant(false);

                return new ExtensionObject(value);
            }
            else
            {
                ArrayValueDataType value = new ArrayValueDataType();
                
                value.BooleanValue = m_generator.GetRandomArray<bool>(false, 10, false);
                value.SByteValue = m_generator.GetRandomArray<sbyte>(false, 10, false);
                value.ByteValue = m_generator.GetRandomArray<byte>(false, 10, false);
                value.Int16Value = m_generator.GetRandomArray<short>(false, 10, false);
                value.UInt16Value = m_generator.GetRandomArray<ushort>(false, 10, false);
                value.Int32Value = m_generator.GetRandomArray<int>(false, 10, false);
                value.UInt32Value = m_generator.GetRandomArray<uint>(false, 10, false);
                value.Int64Value = m_generator.GetRandomArray<long>(false, 10, false);
                value.UInt64Value = m_generator.GetRandomArray<ulong>(false, 10, false);
                value.FloatValue = m_generator.GetRandomArray<float>(false, 10, false);
                value.DoubleValue = m_generator.GetRandomArray<double>(false, 10, false);
                value.StringValue = m_generator.GetRandomArray<string>(false, 10, false);
                value.DateTimeValue = m_generator.GetRandomArray<DateTime>(false, 10, false);
                value.GuidValue = m_generator.GetRandomArray<Uuid>(false, 10, false);
                value.ByteStringValue = m_generator.GetRandomArray<byte[]>(false, 10, false);
                value.XmlElementValue = m_generator.GetRandomArray<XmlElement>(false, 10, false);
                value.NodeIdValue = m_generator.GetRandomArray<Opc.Ua.NodeId>(false, 10, false);
                value.ExpandedNodeIdValue = m_generator.GetRandomArray<ExpandedNodeId>(false, 10, false);
                value.QualifiedNameValue = m_generator.GetRandomArray<QualifiedName>(false, 10, false);
                value.LocalizedTextValue = m_generator.GetRandomArray<LocalizedText>(false, 10, false);
                value.StatusCodeValue = m_generator.GetRandomArray<StatusCode>(false, 10, false);

                object[] values = m_generator.GetRandomArray<object>(false, 10, false);

                for (int ii = 0; values != null && ii < values.Length; ii++)
                {
                    value.VariantValue.Add(new Variant(values[ii]));
                }

                return new ExtensionObject(value);                
            }
        }

        public void StartMonitoringValue(uint monitoredItemId, double samplingInterval, BaseVariableState variable)
        {
            lock (m_lock)
            {
                if (m_monitoredNodes == null)
                {
                    m_monitoredNodes = new Dictionary<uint,BaseVariableState>();
                }

                m_monitoredNodes[monitoredItemId] = variable;

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
                    m_minimumSamplingInterval = Int32.MaxValue;

                    if (m_timer != null)
                    {
                        m_timer.Dispose();
                        m_timer = null;
                    }
                    
                    return;
                }

                if (m_minimumSamplingInterval > samplingInterval)
                {
                    m_minimumSamplingInterval = (int)samplingInterval;

                    if (m_minimumSamplingInterval < 100)
                    {
                        m_minimumSamplingInterval = 100;
                    }
                 
                    if (m_timer != null)
                    {
                        m_timer.Dispose();
                        m_timer = null;
                    }
                    
                    m_timer = new Timer(DoSample, null, m_minimumSamplingInterval, m_minimumSamplingInterval);
                }
            }
        }

        void DoSample(object state)
        {
            Utils.Trace("DoSample HiRes={0:ss.ffff} Now={1:ss.ffff}", HiResClock.UtcNow, DateTime.UtcNow);

            Queue<Sample> samples = new Queue<Sample>();

            lock (m_lock)
            {
                if (m_monitoredNodes == null)
                {
                    return;
                }

                foreach (BaseVariableState variable in m_monitoredNodes.Values)
                {
                    Sample sample = new Sample();

                    sample.Variable = variable;
                    sample.Value = ReadValue(sample.Variable);
                    sample.StatusCode = StatusCodes.Good;
                    sample.Timestamp = DateTime.UtcNow;

                    samples.Enqueue(sample);
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

                if (m_monitoredNodes.Count == 0)
                {
                    SetSamplingInterval(-1);
                }
            }
        }
        
        private class Sample
        {
            public BaseVariableState Variable;
            public object Value;
            public StatusCode StatusCode;
            public DateTime Timestamp;
        }
        
        #region Private Fields
        private object m_lock = new object();
        private ITestDataSystemCallback m_callback;
        private Opc.Ua.Test.DataGenerator m_generator;
        private int m_minimumSamplingInterval;
        private Dictionary<uint,BaseVariableState> m_monitoredNodes;
        private Timer m_timer;
        private StatusCode m_systemStatus;
        private HistoryArchive m_historyArchive;
        #endregion
    }
}
