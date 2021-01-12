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
using System.Collections.Generic;

namespace Opc.Ua.PubSub.PublishedData
{
    /// <summary>
    /// Class specialized in collecting published data 
    /// </summary>
    public class DataCollector
    {
        #region Private Fields
        private Dictionary<string, PublishedDataSetDataType> m_publishedDataSetsByName;
        private IUaPubSubDataStore m_dataStore;
        #endregion

        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="DataCollector"/>.
        /// </summary>
        /// <param name="dataStore">Reference to the <see cref="IUaPubSubDataStore"/> that will be used to collect data.</param>
        public DataCollector(IUaPubSubDataStore dataStore)
        {
            m_dataStore = dataStore;
            m_publishedDataSetsByName = new Dictionary<string, PublishedDataSetDataType>();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Validates a <see cref="PublishedDataSetDataType"/> configuration object.
        /// </summary>
        /// <param name="publishedDataSet">The <see cref="PublishedDataSetDataType"/> that is to be validated.</param>
        /// <returns>true if configuration is correct.</returns>
        public bool ValidatePublishedDataSet(PublishedDataSetDataType publishedDataSet)
        {
            if (publishedDataSet == null)
            {
                throw new ArgumentException(nameof(publishedDataSet));
            }
            if (publishedDataSet.DataSetMetaData == null)
            {
                Utils.Trace(Utils.TraceMasks.Error, "The DataSetMetaData field is null.");
                return false;
            }
            PublishedDataItemsDataType publishedDataItems = ExtensionObject.ToEncodeable(publishedDataSet.DataSetSource) as PublishedDataItemsDataType;
            if (publishedDataItems != null && publishedDataItems.PublishedData != null)
            {
                if (publishedDataItems.PublishedData.Count != publishedDataSet.DataSetMetaData.Fields.Count)
                {
                    Utils.Trace(Utils.TraceMasks.Error, "The DataSetSource.Count is different from DataSetMetaData.Fields.Count.");
                    return false;
                }
            }

            return true;
        }
        /// <summary>
        /// Register a publishedDataSet
        /// </summary>
        /// <param name="publishedDataSet"></param>
        public void AddPublishedDataSet(PublishedDataSetDataType publishedDataSet)
        {
            if (publishedDataSet == null)
            {
                throw new ArgumentException(nameof(publishedDataSet));
            }
            // validate publishedDataSet
            if (ValidatePublishedDataSet(publishedDataSet))
            {
                m_publishedDataSetsByName[publishedDataSet.Name] = publishedDataSet;
            }
            else
            {
                Utils.Trace(Utils.TraceMasks.Error, "The PublishedDataSet {0} was not registered because it is not configured properly.",
                    publishedDataSet.Name);
            }
        }

        /// <summary>
        /// Remove a registered a publishedDataSet
        /// </summary>
        /// <param name="publishedDataSet"></param>
        public void RemovePublishedDataSet(PublishedDataSetDataType publishedDataSet)
        {
            if (publishedDataSet == null)
            {
                throw new ArgumentException(nameof(publishedDataSet));
            }
            if (m_publishedDataSetsByName.ContainsKey(publishedDataSet.Name))
            {
                m_publishedDataSetsByName.Remove(publishedDataSet.Name);
            }
        }

        /// <summary>
        ///  Create and return a DataSet object created from its dataSetName
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public DataSet CollectData(string dataSetName)
        {   
            if(dataSetName == null)
            {
                throw new ArgumentException(nameof(dataSetName));
            }
            if (m_publishedDataSetsByName.ContainsKey(dataSetName))
            {               
                PublishedDataSetDataType publishedDataSet = m_publishedDataSetsByName[dataSetName];
                if (publishedDataSet.DataSetSource != null)
                {
                    DataSet dataSet = new DataSet(dataSetName);
                    PublishedDataItemsDataType publishedDataItems = ExtensionObject.ToEncodeable(publishedDataSet.DataSetSource) as PublishedDataItemsDataType;
                    if (publishedDataItems != null && publishedDataItems.PublishedData != null && publishedDataItems.PublishedData.Count > 0)
                    {
                        dataSet.Fields = new Field[publishedDataItems.PublishedData.Count];
                        for (int i = 0; i < publishedDataItems.PublishedData.Count; i++)
                        {
                            try
                            {
                                PublishedVariableDataType publishedVariable = publishedDataItems.PublishedData[i];
                                dataSet.Fields[i] = new Field();
                                // set FieldMetaData property
                                dataSet.Fields[i].FieldMetaData = publishedDataSet.DataSetMetaData.Fields[i];

                                // retrieve value from DataStore 
                                DataValue dataValue = null;
                                if (publishedVariable.PublishedVariable != null)
                                {
                                    //todo handle missing value in data store
                                    dataValue = m_dataStore.ReadPublishedDataItem(publishedVariable.PublishedVariable, publishedVariable.AttributeId);
                                }

                                if (dataValue == null)
                                {
                                    //try to get the dataValue from ExtensionFields
                                    /*If an entry of the PublishedData references one of the ExtensionFields, the substituteValue shall contain the 
                                    * QualifiedName of the ExtensionFields entry. 
                                    * All other fields of this PublishedVariableDataType array element shall be null*/
                                    QualifiedName extensionFieldName = publishedVariable.SubstituteValue.Value as QualifiedName;
                                    if (extensionFieldName != null)
                                    {
                                        KeyValuePair extensionField = publishedDataSet.ExtensionFields.Find(x => x.Key == extensionFieldName);
                                        if (extensionField != null)
                                        {
                                            dataValue = new DataValue(extensionField.Value);
                                        }
                                    }
                                    if (dataValue == null)
                                    {
                                        dataValue = new DataValue(StatusCodes.Bad, DateTime.UtcNow);
                                    }                                    
                                }
                                else
                                {
                                    //check StatusCode and return SubstituteValue if possible
                                    if (dataValue.StatusCode == StatusCodes.Bad && publishedVariable.SubstituteValue != Variant.Null)
                                    {
                                        dataValue.Value = publishedVariable.SubstituteValue.Value;
                                        dataValue.StatusCode = StatusCodes.UncertainSubstituteValue;
                                    }
                                }
                                dataValue.ServerTimestamp = DateTime.UtcNow;
                                
                                #region FieldMetaData -> MaxStringLength size validation 
                                
                                
                                Field field = dataSet.Fields[i];
                                Variant variant = dataValue.WrappedValue;

                                
                                bool shouldBringToConstraints(uint givenStrlen)
                                {
                                    if (field.FieldMetaData.MaxStringLength > 0 &&
                                        givenStrlen > field.FieldMetaData.MaxStringLength)
                                    {
                                        return true;
                                    }

                                    return false;
                                }

                                switch ((BuiltInType)field.FieldMetaData.BuiltInType)
                                {

                                    case BuiltInType.String:
                                        if (field.FieldMetaData.ValueRank == ValueRanks.Scalar)
                                        {
                                            string strFieldValue = (string)variant.Value;
                                            if (shouldBringToConstraints((uint)strFieldValue.Length))
                                            {
                                                variant.Value = strFieldValue.Substring(0, (int)field.FieldMetaData.MaxStringLength);
                                                dataValue.Value = variant;
                                            }
                                        }
                                        else if (field.FieldMetaData.ValueRank == ValueRanks.OneDimension)
                                        {
                                            string[] valueArray = (string[])variant.Value;
                                            for (int idx = 0; idx < valueArray.Length; idx++)
                                            {
                                                if (shouldBringToConstraints((uint)valueArray[idx].Length))
                                                {
                                                    valueArray[idx] = valueArray[idx].Substring(0, (int)field.FieldMetaData.MaxStringLength);
                                                }
                                            }
                                            dataValue.Value = valueArray;
                                        }
                                        break;
                                    case BuiltInType.ByteString:
                                        if (field.FieldMetaData.ValueRank == ValueRanks.Scalar)
                                        {
                                            byte[] byteStringFieldValue = (byte[])variant.Value;
                                            if (shouldBringToConstraints((uint)byteStringFieldValue.Length))
                                            {
                                                byte[] byteArray = (byte[])byteStringFieldValue.Clone();
                                                Array.Resize(ref byteArray, (int)field.FieldMetaData.MaxStringLength);
                                                variant.Value = byteArray;
                                                dataValue.Value = variant;
                                            }
                                        }
                                        else if (field.FieldMetaData.ValueRank == ValueRanks.OneDimension)
                                        {
                                            byte[][] valueArray = (byte[][])variant.Value;
                                            for (int idx = 0; idx < valueArray.Length; idx++)
                                            {
                                                if (shouldBringToConstraints((uint)valueArray[idx].Length))
                                                {
                                                    byte[] byteArray = (byte[])valueArray[idx].Clone();
                                                    Array.Resize(ref byteArray, (int)field.FieldMetaData.MaxStringLength);
                                                    valueArray[idx] = byteArray;
                                                }
                                            }
                                            dataValue.Value = valueArray;
                                            
                                        }
                                        break;
                                    default:
                                        break;
                                }

                                #endregion

                                dataSet.Fields[i].Value = dataValue;
                            }
                            catch(Exception ex)
                            {
                                dataSet.Fields[i].Value = new DataValue(StatusCodes.Bad, DateTime.UtcNow);
                                Utils.Trace(Utils.TraceMasks.Information, "DataCollector.CollectData for dataset {0} field {1} resulted in ex {2}",
                                    dataSetName, i, ex);
                            }
                        }
                        return dataSet;
                    }
                    
                }
            }
            return null;
        }        
        #endregion
    }
}
