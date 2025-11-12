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
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.PubSub;

namespace Quickstarts.ConsoleReferencePublisher
{
    internal sealed class PublishedValuesWrites
    {
        /// <summary>
        /// It should match the namespace index from configuration file
        /// </summary>
        public const ushort NamespaceIndexSimple = 2;
        public const ushort NamespaceIndexAllTypes = 3;

        private const string kDataSetNameSimple = "Simple";
        private const string kDataSetNameAllTypes = "AllTypes";

        /// <summary>
        /// simulate for BoolToogle changes to 3 seconds
        /// </summary>
        private int m_boolToogleCount;
        private const int kBoolToogleLimit = 2;
        private const int kSimpleInt32Limit = 10000;

        private readonly FieldMetaDataCollection m_simpleFields = [];
        private readonly FieldMetaDataCollection m_allTypesFields = [];

        private readonly ILogger m_logger;
        private readonly PublishedDataSetDataTypeCollection m_publishedDataSets;
        private readonly IUaPubSubDataStore m_dataStore;
        private Timer m_updateValuesTimer;

        private readonly string[] m_aviationAlphabet =
        [
            "Alfa",
            "Bravo",
            "Charlie",
            "Delta",
            "Echo",
            "Foxtrot",
            "Golf",
            "Hotel",
            "India",
            "Juliet",
            "Kilo",
            "Lima",
            "Mike",
            "November",
            "Oscar",
            "Papa",
            "Quebec",
            "Romeo",
            "Sierra",
            "Tango",
            "Uniform",
            "Victor",
            "Whiskey",
            "X-Ray",
            "Yankee",
            "Zulu"
        ];

        private int m_aviationAlphabetIndex;
        private readonly Lock m_lock = new();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="uaPubSubApplication"></param>
        public PublishedValuesWrites(UaPubSubApplication uaPubSubApplication, ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<PublishedValuesWrites>();
            m_publishedDataSets = uaPubSubApplication.UaPubSubConfigurator.PubSubConfiguration
                .PublishedDataSets;
            m_dataStore = uaPubSubApplication.DataStore;
        }

        public void Dispose()
        {
            m_updateValuesTimer.Dispose();
        }

        /// <summary>
        /// Initialize PublisherData with information from configuration and start timer to update data
        /// </summary>
        public void Start()
        {
            if (m_publishedDataSets != null)
            {
                // Remember the fields to be updated
                foreach (PublishedDataSetDataType publishedDataSet in m_publishedDataSets)
                {
                    switch (publishedDataSet.Name)
                    {
                        case kDataSetNameSimple:
                            m_simpleFields.AddRange(publishedDataSet.DataSetMetaData.Fields);
                            break;
                        case kDataSetNameAllTypes:
                            m_allTypesFields.AddRange(publishedDataSet.DataSetMetaData.Fields);
                            break;
                        default:
                            m_logger.LogInformation(
                                "PublishedValuesWrites.Start: {DataSet} unknown.",
                                publishedDataSet.Name);
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
                m_logger.LogError(e,
                    "SamplePublisher.DataStoreValuesGenerator.LoadInitialData wrong field");
            }

            m_updateValuesTimer = new Timer(UpdateValues, null, 1000, 1000);
        }

        /// <summary>
        /// Load initial demo data
        /// </summary>
        private void LoadInitialData()
        {
            WriteFieldData(
                "BoolToggle",
                NamespaceIndexSimple,
                new DataValue(new Variant(false), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "Int32",
                NamespaceIndexSimple,
                new DataValue(new Variant(0), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "Int32Fast",
                NamespaceIndexSimple,
                new DataValue(new Variant(0), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "DateTime",
                NamespaceIndexSimple,
                new DataValue(new Variant(DateTime.UtcNow), StatusCodes.Good, DateTime.UtcNow)
            );

            WriteFieldData(
                "BoolToggle",
                NamespaceIndexAllTypes,
                new DataValue(new Variant(true), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "Byte",
                NamespaceIndexAllTypes,
                new DataValue(new Variant((byte)0), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "Int16",
                NamespaceIndexAllTypes,
                new DataValue(new Variant((short)0), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "Int32",
                NamespaceIndexAllTypes,
                new DataValue(new Variant(0), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "SByte",
                NamespaceIndexAllTypes,
                new DataValue(new Variant((sbyte)0), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "UInt16",
                NamespaceIndexAllTypes,
                new DataValue(new Variant((ushort)0), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "UInt32",
                NamespaceIndexAllTypes,
                new DataValue(new Variant((uint)0), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "UInt64",
                NamespaceIndexAllTypes,
                new DataValue(new Variant((ulong)0), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "Float",
                NamespaceIndexAllTypes,
                new DataValue(new Variant((float)0F), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "Double",
                NamespaceIndexAllTypes,
                new DataValue(new Variant((double)0.0), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "String",
                NamespaceIndexAllTypes,
                new DataValue(new Variant(m_aviationAlphabet[0]), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "ByteString",
                NamespaceIndexAllTypes,
                new DataValue(
                    new Variant(new byte[] { 1, 2, 3 }),
                    StatusCodes.Good,
                    DateTime.UtcNow)
            );
            WriteFieldData(
                "Guid",
                NamespaceIndexAllTypes,
                new DataValue(new Variant(Guid.NewGuid()), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "DateTime",
                NamespaceIndexAllTypes,
                new DataValue(new Variant(DateTime.UtcNow), StatusCodes.Good, DateTime.UtcNow)
            );
            WriteFieldData(
                "UInt32Array",
                NamespaceIndexAllTypes,
                new DataValue(
                    new Variant(new uint[] { 1, 2, 3 }),
                    StatusCodes.Good,
                    DateTime.UtcNow)
            );
        }

        /// <summary>
        /// Write (update) field data
        /// </summary>
        /// <param name="metaDatafieldName"></param>
        /// <param name="dataValue"></param>
        private void WriteFieldData(
            string metaDatafieldName,
            ushort namespaceIndex,
            DataValue dataValue)
        {
            m_dataStore.WritePublishedDataItem(
                new NodeId(metaDatafieldName, namespaceIndex),
                Attributes.Value,
                dataValue
            );
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
                                if (m_boolToogleCount >= kBoolToogleLimit)
                                {
                                    m_boolToogleCount = 0;
                                    IncrementValue(variable, NamespaceIndexSimple);
                                }
                                break;
                            case "Int32":
                                IncrementValue(variable, NamespaceIndexSimple, kSimpleInt32Limit);
                                break;
                            case "Int32Fast":
                                IncrementValue(
                                    variable,
                                    NamespaceIndexSimple,
                                    kSimpleInt32Limit,
                                    100);
                                break;
                            case "DateTime":
                                IncrementValue(variable, NamespaceIndexSimple);
                                break;
                            default:
                                m_logger.LogDebug("{Variable} not processed.", variable.Name);
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
                m_logger.LogError(e, "Unexpected error doing simulation.");
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
        /// <exception cref="ServiceResultException"></exception>
        private void IncrementValue(
            FieldMetaData variable,
            ushort namespaceIndex,
            long maxAllowedValue = int.MaxValue,
            int step = 0
        )
        {
            // Read value to be incremented
            DataValue dataValue = m_dataStore.ReadPublishedDataItem(
                new NodeId(variable.Name, namespaceIndex),
                Attributes.Value
            );
            if (dataValue.Value == null)
            {
                return;
            }

            bool valueUpdated = false;

            BuiltInType builtInType = TypeInfo.GetBuiltInType(variable.DataType);
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        bool boolValue = Convert.ToBoolean(
                            dataValue.Value,
                            CultureInfo.InvariantCulture);
                        dataValue.Value = !boolValue;
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.Byte:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        byte byteValue = Convert.ToByte(
                            dataValue.Value,
                            CultureInfo.InvariantCulture);
                        dataValue.Value = (byte)(byteValue + 1);
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.Int16:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        int intIdentifier = Convert.ToInt16(
                            dataValue.Value,
                            CultureInfo.InvariantCulture);
                        Interlocked.CompareExchange(ref intIdentifier, 0, short.MaxValue);
                        dataValue.Value = (short)Interlocked.Increment(ref intIdentifier);
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.Int32:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        int int32Value = Convert.ToInt32(
                            dataValue.Value,
                            CultureInfo.InvariantCulture);
                        if (step > 0)
                        {
                            int32Value += step - 1;
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
                        int intIdentifier = Convert.ToSByte(
                            dataValue.Value,
                            CultureInfo.InvariantCulture);
                        Interlocked.CompareExchange(ref intIdentifier, 0, sbyte.MaxValue);
                        dataValue.Value = (sbyte)Interlocked.Increment(ref intIdentifier);
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.UInt16:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        int intIdentifier = Convert.ToUInt16(
                            dataValue.Value,
                            CultureInfo.InvariantCulture);
                        Interlocked.CompareExchange(ref intIdentifier, 0, ushort.MaxValue);
                        dataValue.Value = (ushort)Interlocked.Increment(ref intIdentifier);
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.UInt32:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        long longIdentifier = Convert.ToUInt32(
                            dataValue.Value,
                            CultureInfo.InvariantCulture);
                        Interlocked.CompareExchange(ref longIdentifier, 0, uint.MaxValue);
                        dataValue.Value = (uint)Interlocked.Increment(ref longIdentifier);
                        valueUpdated = true;
                    }
                    else if (variable.ValueRank == ValueRanks.OneDimension)
                    {
                        if (dataValue.Value is uint[] values)
                        {
                            for (int i = 0; i < values.Length; i++)
                            {
                                long longIdentifier = values[i];
                                Interlocked.CompareExchange(ref longIdentifier, 0, uint.MaxValue);
                                values[i] = (uint)Interlocked.Increment(ref longIdentifier);
                            }
                            valueUpdated = true;
                        }
                    }
                    break;
                case BuiltInType.UInt64:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        ulong uint64Value = Convert.ToUInt64(
                            dataValue.Value,
                            CultureInfo.InvariantCulture);
                        float longIdentifier = uint64Value + 1;
                        Interlocked.CompareExchange(ref longIdentifier, 0, ulong.MaxValue);
                        dataValue.Value = (ulong)longIdentifier;
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.Float:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        float floatValue = Convert.ToSingle(
                            dataValue.Value,
                            CultureInfo.InvariantCulture);
                        Interlocked.CompareExchange(ref floatValue, 0, float.MaxValue);
                        dataValue.Value = floatValue + 1;
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.Double:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        double doubleValue = Convert.ToDouble(
                            dataValue.Value,
                            CultureInfo.InvariantCulture);
                        Interlocked.CompareExchange(ref doubleValue, 0, double.MaxValue);
                        dataValue.Value = doubleValue + 1;
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
                case BuiltInType.Guid:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        dataValue.Value = Guid.NewGuid();
                        valueUpdated = true;
                    }
                    break;
                case BuiltInType.String:
                    if (variable.ValueRank == ValueRanks.Scalar)
                    {
                        m_aviationAlphabetIndex = (m_aviationAlphabetIndex + 1) %
                            m_aviationAlphabet.Length;
                        dataValue.Value = m_aviationAlphabet[m_aviationAlphabetIndex];
                        valueUpdated = true;
                    }
                    break;
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    break;
                default:
                    throw ServiceResultException.Unexpected($"Unexpected BuiltInType {builtInType}");
            }

            if (valueUpdated)
            {
                // Save new updated value to data store
                WriteFieldData(variable.Name, namespaceIndex, dataValue);
            }
        }
    }
}
