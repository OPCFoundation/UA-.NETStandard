/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using Opc.Ua;
using Opc.Ua.PubSub;

namespace Quickstarts.ConsoleReferencePublisher
{
    class PublishedValuesWrites
    {
        #region Fields
        // It should match the namespace index from configuration file
        public const ushort NamespaceIndexSimple = 2;
        public const ushort NamespaceIndexAllTypes = 3;

        private const string DataSetNameSimple = "Simple";
        private const string DataSetNameAllTypes = "AllTypes";

        // simulate for BoolToogle changes to 3 seconds
        private int m_boolToogleCount = 0;
        private const int BoolToogleLimit = 2;
        private const int SimpleInt32Limit = 10000;

        private FieldMetaDataCollection m_simpleFields = new FieldMetaDataCollection();
        private FieldMetaDataCollection m_allTypesFields = new FieldMetaDataCollection();

        private PublishedDataSetDataTypeCollection m_publishedDataSets;
        private IUaPubSubDataStore m_dataStore;
        private Timer m_updateValuesTimer;

        private object m_lock = new object();

        #endregion

        #region Constructor
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="pubSubApplication"></param>
        public PublishedValuesWrites(UaPubSubApplication uaPubSubApplication)
        {
            m_publishedDataSets = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration.PublishedDataSets;
            m_dataStore = uaPubSubApplication.DataStore;
        }
        #endregion

        #region IDisposable

        public void Dispose()
        {
            m_updateValuesTimer.Dispose();
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Initialize PublisherData with information from configuration and start timer to update data 
        /// </summary>
        public void Start()
        {
            if (m_publishedDataSets != null)
            {
                // Remember the fields to be updated 
                foreach (var publishedDataSet in m_publishedDataSets)
                {
                    switch (publishedDataSet.Name)
                    {
                        case DataSetNameSimple:
                            m_simpleFields.AddRange(publishedDataSet.DataSetMetaData.Fields);
                            break;
                        case DataSetNameAllTypes:
                            m_allTypesFields.AddRange(publishedDataSet.DataSetMetaData.Fields);
                            break;
                    }
                }
            }

            try
            {
                LoadInitialData();
            }
            catch (Exception e)
            {
                Utils.Trace(Utils.TraceMasks.Error, "SamplePublisher.DataStoreValuesGenerator.LoadInitialData wrong field: {0}", e.StackTrace);
            }

            m_updateValuesTimer = new Timer(UpdateValues, null, 1000, 1000);
        }

        #endregion

        #region Private Methods
        /// <summary>
        /// Load initial demo data
        /// </summary>
        private void LoadInitialData()
        {
            #region DataSet 'Simple' fill with data
            WriteFieldData("BoolToggle", NamespaceIndexSimple, new DataValue(new Variant(false), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("Int32", NamespaceIndexSimple, new DataValue(new Variant(0), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("Int32Fast", NamespaceIndexSimple, new DataValue(new Variant(0), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("DateTime", NamespaceIndexSimple, new DataValue(new Variant(DateTime.UtcNow), StatusCodes.Good, DateTime.UtcNow));
            #endregion

            #region DataSet 'AllTypes' fill with data

            WriteFieldData("BoolToggle", NamespaceIndexAllTypes, new DataValue(new Variant(true), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("Byte", NamespaceIndexAllTypes, new DataValue(new Variant((byte)0), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("Int16", NamespaceIndexAllTypes, new DataValue(new Variant((Int16)0), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("Int32", NamespaceIndexAllTypes, new DataValue(new Variant(0), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("SByte", NamespaceIndexAllTypes, new DataValue(new Variant((sbyte)0), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("UInt16", NamespaceIndexAllTypes, new DataValue(new Variant((UInt16)0), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("UInt32", NamespaceIndexAllTypes, new DataValue(new Variant((UInt32)0), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("Float", NamespaceIndexAllTypes, new DataValue(new Variant((float)0F), StatusCodes.Good, DateTime.UtcNow));
            WriteFieldData("Double", NamespaceIndexAllTypes, new DataValue(new Variant((double)0.0), StatusCodes.Good, DateTime.UtcNow));
            #endregion
        }

        /// <summary>
        /// Write (update) field data
        /// </summary>
        /// <param name="metaDatafieldName"></param>
        /// <param name="dataValue"></param>
        private void WriteFieldData(string metaDatafieldName, ushort namespaceIndex, DataValue dataValue)
        {
            m_dataStore.WritePublishedDataItem(new NodeId(metaDatafieldName, namespaceIndex), Attributes.Value, dataValue);
        }

        /// <summary>
        /// Simulate value changes in dynamic nodes
        /// </summary>
        /// <param name="state"></param>
        private void UpdateValues(object state)
        {
            try
            {
                lock (m_lock)
                {
                    foreach (FieldMetaData variable in m_simpleFields)
                    {
                        switch (variable.Name)
                        {
                            case "BoolToggle":
                                m_boolToogleCount++;
                                if (m_boolToogleCount >= BoolToogleLimit)
                                {
                                    m_boolToogleCount = 0;
                                    IncrementValue(variable, NamespaceIndexSimple);
                                }
                                break;
                            case "Int32":
                                IncrementValue(variable, NamespaceIndexSimple, SimpleInt32Limit);
                                break;
                            case "Int32Fast":
                                IncrementValue(variable, NamespaceIndexSimple, SimpleInt32Limit, 100);
                                break;
                            case "DateTime":
                                IncrementValue(variable, NamespaceIndexSimple);
                                break;
                        }
                    }

                    foreach (FieldMetaData variable in m_allTypesFields)
                    {
                        IncrementValue(variable, NamespaceIndexAllTypes);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error doing simulation.");
            }
        }

        /// <summary>
        /// Increment value
        /// maxAllowedValue - maximum incremented value before reset value to initial value
        /// step - the increment value
        /// </summary>
        /// <param name="variable"></param>
        /// <param name="namespaceIndex"></param>
        /// <param name="maxAllowedValue"></param>
        /// <param name="step"></param>
        private void IncrementValue(FieldMetaData variable, ushort namespaceIndex, long maxAllowedValue = Int32.MaxValue, int step = 0)
        {
            // Read value to be incremented
            DataValue dataValue = m_dataStore.ReadPublishedDataItem(new NodeId(variable.Name, namespaceIndex), Attributes.Value);
            if (dataValue.Value == null)
            {
                return;
            }

            bool valueUpdated = false;

            BuiltInType expectedType = TypeInfo.GetBuiltInType(variable.DataType);
            switch (expectedType)
            {
                case BuiltInType.Boolean:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        Boolean boolValue = Convert.ToBoolean(dataValue.Value);
                        dataValue.Value = !boolValue;
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.Byte:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        Byte byteValue = Convert.ToByte(dataValue.Value);
                        dataValue.Value = ++byteValue;
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.Int16:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        Int16 int16Value = Convert.ToInt16(dataValue.Value);
                        int intIdentifier = int16Value;
                        Interlocked.CompareExchange(ref intIdentifier, 0, Int16.MaxValue);
                        dataValue.Value = (Int16)Interlocked.Increment(ref intIdentifier);
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.Int32:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        Int32 int32Value = Convert.ToInt32(dataValue.Value);
                        if (step > 0)
                        {
                            int32Value += (step - 1);
                        }
                        if (int32Value > maxAllowedValue)
                        {
                            int32Value = 0;
                        }
                        dataValue.Value = Interlocked.Increment(ref int32Value);
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.SByte:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        SByte sbyteValue = Convert.ToSByte(dataValue.Value);
                        int intIdentifier = sbyteValue;
                        Interlocked.CompareExchange(ref intIdentifier, 0, SByte.MaxValue);
                        dataValue.Value = (SByte)Interlocked.Increment(ref intIdentifier);
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.UInt16:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        UInt16 uint16Value = Convert.ToUInt16(dataValue.Value);
                        int intIdentifier = uint16Value;
                        Interlocked.CompareExchange(ref intIdentifier, 0, UInt16.MaxValue);
                        dataValue.Value = (UInt16)Interlocked.Increment(ref intIdentifier);
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.UInt32:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        UInt32 uint32Value = Convert.ToUInt32(dataValue.Value);
                        long longIdentifier = uint32Value;
                        Interlocked.CompareExchange(ref longIdentifier, 0, UInt32.MaxValue);
                        dataValue.Value = (UInt32)Interlocked.Increment(ref longIdentifier);
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.Float:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        float floatValue = Convert.ToSingle(dataValue.Value);
                        Interlocked.CompareExchange(ref floatValue, 0, float.MaxValue);
                        dataValue.Value = ++floatValue;
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.Double:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        double doubleValue = Convert.ToDouble(dataValue.Value);
                        Interlocked.CompareExchange(ref doubleValue, 0, double.MaxValue);
                        dataValue.Value = ++doubleValue;
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.DateTime:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        dataValue.Value = DateTime.UtcNow;
                        valueUpdated = true;
                    }
                    break;
            }

            if (valueUpdated)
            {
                // Save new updated value to data store
                WriteFieldData(variable.Name, namespaceIndex, dataValue);
            }
        }
        #endregion
    }
}
