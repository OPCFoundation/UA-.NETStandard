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

namespace Opc.Ua.PubSub.PublishedData
{
    /// <summary>
    /// Class specialized in collecting published data
    /// </summary>
    public class DataCollector
    {
        private readonly Dictionary<string, PublishedDataSetDataType> m_publishedDataSetsByName;
        private readonly IUaPubSubDataStore m_dataStore;

        /// <summary>
        /// Create new instance of <see cref="DataCollector"/>.
        /// </summary>
        /// <param name="dataStore">Reference to the <see cref="IUaPubSubDataStore"/> that will be used to collect data.</param>
        public DataCollector(IUaPubSubDataStore dataStore)
        {
            m_dataStore = dataStore;
            m_publishedDataSetsByName = [];
        }

        /// <summary>
        /// Validates a <see cref="PublishedDataSetDataType"/> configuration object.
        /// </summary>
        /// <param name="publishedDataSet">The <see cref="PublishedDataSetDataType"/> that is to be validated.</param>
        /// <returns>true if configuration is correct.</returns>
        /// <exception cref="ArgumentException"><paramref name="publishedDataSet"/></exception>
        public bool ValidatePublishedDataSet(PublishedDataSetDataType publishedDataSet)
        {
            if (publishedDataSet == null)
            {
                throw new ArgumentException(null, nameof(publishedDataSet));
            }
            if (publishedDataSet.DataSetMetaData == null)
            {
                Utils.Trace(Utils.TraceMasks.Error, "The DataSetMetaData field is null.");
                return false;
            }
            if (ExtensionObject.ToEncodeable(publishedDataSet.DataSetSource)
                    is PublishedDataItemsDataType publishedDataItems &&
                publishedDataItems.PublishedData != null &&
                publishedDataItems.PublishedData.Count != publishedDataSet.DataSetMetaData.Fields
                    .Count)
            {
                Utils.Trace(
                    Utils.TraceMasks.Error,
                    "The DataSetSource.Count is different from DataSetMetaData.Fields.Count.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Register a publishedDataSet
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="publishedDataSet"/></exception>
        public void AddPublishedDataSet(PublishedDataSetDataType publishedDataSet)
        {
            if (publishedDataSet == null)
            {
                throw new ArgumentException(null, nameof(publishedDataSet));
            }
            // validate publishedDataSet
            if (ValidatePublishedDataSet(publishedDataSet))
            {
                m_publishedDataSetsByName[publishedDataSet.Name] = publishedDataSet;
            }
            else
            {
                Utils.Trace(
                    Utils.TraceMasks.Error,
                    "The PublishedDataSet {0} was not registered because it is not configured properly.",
                    publishedDataSet.Name);
            }
        }

        /// <summary>
        /// Remove a registered a publishedDataSet
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="publishedDataSet"/></exception>
        public void RemovePublishedDataSet(PublishedDataSetDataType publishedDataSet)
        {
            if (publishedDataSet == null)
            {
                throw new ArgumentException(null, nameof(publishedDataSet));
            }
            m_publishedDataSetsByName.Remove(publishedDataSet.Name);
        }

        /// <summary>
        ///  Create and return a DataSet object created from its dataSetName
        /// </summary>
        public DataSet CollectData(string dataSetName)
        {
            PublishedDataSetDataType publishedDataSet = GetPublishedDataSet(dataSetName);

            if (publishedDataSet != null)
            {
                m_dataStore.UpdateMetaData(publishedDataSet);

                if (publishedDataSet.DataSetSource != null)
                {
                    var dataSet = new DataSet(dataSetName)
                    {
                        DataSetMetaData = publishedDataSet.DataSetMetaData
                    };

                    if (ExtensionObject.ToEncodeable(publishedDataSet.DataSetSource)
                            is PublishedDataItemsDataType publishedDataItems &&
                        publishedDataItems.PublishedData != null &&
                        publishedDataItems.PublishedData.Count > 0)
                    {
                        dataSet.Fields = new Field[publishedDataItems.PublishedData.Count];
                        for (int i = 0; i < publishedDataItems.PublishedData.Count; i++)
                        {
                            try
                            {
                                PublishedVariableDataType publishedVariable = publishedDataItems
                                    .PublishedData[i];
                                dataSet.Fields[i] = new Field
                                {
                                    // set FieldMetaData property
                                    FieldMetaData = publishedDataSet.DataSetMetaData.Fields[i]
                                };

                                // retrieve value from DataStore
                                DataValue dataValue = null;

                                if (publishedVariable.PublishedVariable != null)
                                {
                                    dataValue = m_dataStore.ReadPublishedDataItem(
                                        publishedVariable.PublishedVariable,
                                        publishedVariable.AttributeId);
                                }

                                if (dataValue == null)
                                {
                                    //try to get the dataValue from ExtensionFields
                                    /*If an entry of the PublishedData references one of the ExtensionFields, the substituteValue shall contain the
                                    * QualifiedName of the ExtensionFields entry.
                                    * All other fields of this PublishedVariableDataType array element shall be null*/
                                    var extensionFieldName = publishedVariable.SubstituteValue
                                        .Value as QualifiedName;
                                    if (extensionFieldName != null)
                                    {
                                        KeyValuePair extensionField = publishedDataSet
                                            .ExtensionFields
                                            .Find(x =>
                                                x.Key == extensionFieldName);
                                        if (extensionField != null)
                                        {
                                            dataValue = new DataValue(extensionField.Value);
                                        }
                                    }
                                    dataValue ??= new DataValue(StatusCodes.Bad, DateTime.UtcNow);
                                }
                                else
                                {
                                    dataValue = Utils.Clone(dataValue);

                                    //check StatusCode and return SubstituteValue if possible
                                    if (dataValue.StatusCode == StatusCodes.Bad &&
                                        publishedVariable.SubstituteValue != Variant.Null)
                                    {
                                        dataValue.Value = publishedVariable.SubstituteValue.Value;
                                        dataValue.StatusCode = StatusCodes.UncertainSubstituteValue;
                                    }
                                }

                                dataValue.ServerTimestamp = DateTime.UtcNow;

                                Field field = dataSet.Fields[i];
                                Variant variant = dataValue.WrappedValue;

                                bool ShouldBringToConstraints(uint givenStrlen)
                                {
                                    return field.FieldMetaData.MaxStringLength > 0 &&
                                        givenStrlen > field.FieldMetaData.MaxStringLength;
                                }

                                switch ((BuiltInType)field.FieldMetaData.BuiltInType)
                                {
                                    case BuiltInType.String:
                                        if (field.FieldMetaData.ValueRank == ValueRanks.Scalar)
                                        {
                                            if (variant.Value is string strFieldValue &&
                                                ShouldBringToConstraints(
                                                    (uint)strFieldValue.Length))
                                            {
                                                variant.Value = strFieldValue[
                                                    ..(int)field.FieldMetaData.MaxStringLength
                                                ];
                                                dataValue.Value = variant;
                                            }
                                        }
                                        else if (field.FieldMetaData.ValueRank == ValueRanks
                                            .OneDimension)
                                        {
                                            string[] valueArray = variant.Value as string[];
                                            if (valueArray != null)
                                            {
                                                for (int idx = 0; idx < valueArray.Length; idx++)
                                                {
                                                    if (ShouldBringToConstraints(
                                                        (uint)valueArray[idx].Length))
                                                    {
                                                        valueArray[idx] = valueArray[idx][
                                                            ..(int)field.FieldMetaData
                                                                .MaxStringLength
                                                        ];
                                                    }
                                                }
                                            }
                                            dataValue.Value = valueArray;
                                        }
                                        break;
                                    case BuiltInType.ByteString:
                                        if (field.FieldMetaData.ValueRank == ValueRanks.Scalar)
                                        {
                                            if (variant.Value is byte[] byteStringFieldValue &&
                                                ShouldBringToConstraints(
                                                    (uint)byteStringFieldValue.Length))
                                            {
                                                byte[] byteArray = (byte[])byteStringFieldValue
                                                    .Clone();
                                                Array.Resize(
                                                    ref byteArray,
                                                    (int)field.FieldMetaData.MaxStringLength);
                                                variant.Value = byteArray;
                                                dataValue.Value = variant;
                                            }
                                        }
                                        else if (field.FieldMetaData.ValueRank == ValueRanks
                                            .OneDimension)
                                        {
                                            byte[][] valueArray = variant.Value as byte[][];
                                            if (valueArray != null)
                                            {
                                                for (int idx = 0; idx < valueArray.Length; idx++)
                                                {
                                                    if (ShouldBringToConstraints(
                                                        (uint)valueArray[idx].Length))
                                                    {
                                                        byte[] byteArray = (byte[])valueArray[idx]
                                                            .Clone();
                                                        Array.Resize(
                                                            ref byteArray,
                                                            (int)field.FieldMetaData
                                                                .MaxStringLength);
                                                        valueArray[idx] = byteArray;
                                                    }
                                                }
                                            }
                                            dataValue.Value = valueArray;
                                        }
                                        break;
                                }

                                dataSet.Fields[i].Value = dataValue;
                            }
                            catch (Exception ex)
                            {
                                dataSet.Fields[i].Value
                                    = new DataValue(StatusCodes.Bad, DateTime.UtcNow);
                                Utils.Trace(
                                    Utils.TraceMasks.Information,
                                    "DataCollector.CollectData for dataset {0} field {1} resulted in ex {2}",
                                    dataSetName,
                                    i,
                                    ex);
                            }
                        }
                        return dataSet;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Get The <see cref="PublishedDataSetDataType"/> for a DataSetName
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="dataSetName"/></exception>
        public PublishedDataSetDataType GetPublishedDataSet(string dataSetName)
        {
            if (dataSetName == null)
            {
                throw new ArgumentException(null, nameof(dataSetName));
            }

            if (m_publishedDataSetsByName.TryGetValue(
                dataSetName,
                out PublishedDataSetDataType value))
            {
                return value;
            }
            return null;
        }
    }
}
