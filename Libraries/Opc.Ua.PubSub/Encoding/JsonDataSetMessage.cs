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

using Opc.Ua.PubSub.PublishedData;
using System;
using System.Collections.Generic;
using System.Xml;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// The UADPDataSetMessage class handler.
    /// It handles the UADPDataSetMessage encoding 
    /// </summary>
    internal class JsonDataSetMessage : UaDataSetMessage
    {
        #region Fields
        private const string FieldDataSetWriterId = "DataSetWriterId";
        private const string FieldSequenceNumber = "SequenceNumber";
        private const string FieldMetaDataVersion = "MetaDataVersion";
        private const string FieldTimestamp = "Timestamp";
        private const string FieldStatus = "Status";
        private const string FieldPayload = "Payload";

        private DataSet m_dataSet;
        private FieldTypeEncodingMask m_fieldTypeEncoding;
         
        #endregion

        #region Constructors

        /// <summary>
        /// Constructor
        /// </summary>
        public JsonDataSetMessage()
        {
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Constructor with DataSet parameter
        /// </summary>
        /// <param name="dataSet"></param>        
        public JsonDataSetMessage(DataSet dataSet = null) : this()
        {
            m_dataSet = dataSet;
        }

        #endregion

        #region Properties

        #region Inherited from DatasetWriter

        /// <summary>
        /// Get and Set corresponding DataSetWriterId
        /// </summary>
        public ushort DataSetWriterId { get; set; }

        /// <summary>
        /// Get DataSetFieldContentMask
        /// This DataType defines flags to include DataSet field related information like status and 
        /// timestamp in addition to the value in the DataSetMessage.
        /// </summary>
        public DataSetFieldContentMask FieldContentMask { get; private set; }

        /// <summary>
        /// Get JsonDataSetMessageContentMask
        /// The DataSetWriterMessageContentMask defines the flags for the content of the DataSetMessage header.
        /// The Json message mapping specific flags are defined by the <see cref="JsonDataSetMessageContentMask"/> enum.
        /// </summary>
        public JsonDataSetMessageContentMask DataSetMessageContentMask { get; private set; }

        /// <summary>
        /// Flag that indicates if the dataset message header is encoded
        /// </summary>
        public bool HasDataSetMessageHeader { get; set; }
        #endregion

        #region DataSetMessage settings
        /// <summary>
        /// Get and Set SequenceNumber
        /// A strictly monotonically increasing sequence number assigned by the publisher to each DataSetMessage sent.
        /// </summary>
        public uint SequenceNumber { get; set; }

        /// <summary>
        /// The version of the DataSetMetaData which describes the contents of the Payload.
        /// </summary>
        public ConfigurationVersionDataType MetaDataVersion { get; set; }        

        /// <summary>
        /// Get and Set Timestamp
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Get and Set Status
        /// </summary>
        public StatusCode Status { get; set; }

        /// <summary>
        /// Get DataSet
        /// </summary>
        public DataSet DataSet
        {
            get { return m_dataSet; }
        }

        #endregion
        #endregion Properties

        #region Public Methods

        /// <summary>
        /// Set DataSetFieldContentMask 
        /// </summary>
        /// <param name="fieldContentMask"></param>
        public void SetFieldContentMask(DataSetFieldContentMask fieldContentMask)
        {
            FieldContentMask = fieldContentMask;

            if (FieldContentMask == DataSetFieldContentMask.None)
            {
                // 00 Variant Field Encoding
                m_fieldTypeEncoding = FieldTypeEncodingMask.Variant;
            }
            else if ((FieldContentMask & (DataSetFieldContentMask.StatusCode
                                          | DataSetFieldContentMask.SourceTimestamp
                                          | DataSetFieldContentMask.ServerTimestamp
                                          | DataSetFieldContentMask.SourcePicoSeconds
                                          | DataSetFieldContentMask.ServerPicoSeconds)) != 0)
            {
                // 10 DataValue Field Encoding
                m_fieldTypeEncoding = FieldTypeEncodingMask.DataValue;
            }
            else if ((FieldContentMask & DataSetFieldContentMask.RawData) != 0)
            {
                // 01 RawData Field Encoding
                m_fieldTypeEncoding = FieldTypeEncodingMask.RawData;
            }
        }

        /// <summary>
        /// Set MessageContentMask 
        /// </summary>
        /// <param name="messageContentMask"></param>
        public void SetMessageContentMask(JsonDataSetMessageContentMask messageContentMask)
        {
            DataSetMessageContentMask = messageContentMask;
        }
        /// <summary>
        /// Encode dataset message
        /// </summary>
        /// <param name="jsonEncoder"></param>
        public void Encode(JsonEncoder jsonEncoder)
        {
            jsonEncoder.PushStructure(null);
            if (HasDataSetMessageHeader)
            {
                EncodeDataSetMessageHeader(jsonEncoder);
            }

            if (DataSet != null)
            {
                EncodePayload(jsonEncoder);
            }

            jsonEncoder.PopStructure();
        }

        /// <summary>
        /// Decode dataset
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="messagesCount"></param>
        /// <param name="messagesListName"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        public DataSet DecodePossibleDataSetReader(JsonDecoder jsonDecoder, int messagesCount, string messagesListName, DataSetReaderDataType dataSetReader)
        {
           // foreach(object message in messagesList)
           for (int index = 0; index < messagesCount; index++)
            {
                bool wasPush = jsonDecoder.PushArray(messagesListName, index);
                if (wasPush)
                {
                    // atempt decoding the DataSet fields
                    DataSet dataSet = DecodePossibleDataSetReader(jsonDecoder, dataSetReader);
                    // redo jsonDecoder stack
                    jsonDecoder.Pop();

                    if (dataSet != null)
                    {
                        return dataSet;
                    }                    
                }
            }
            return null;
        }

        /// <summary>
        /// Decode dataset from the Keyvalue pairs
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        public DataSet DecodePossibleDataSetReader(JsonDecoder jsonDecoder, DataSetReaderDataType dataSetReader)
        {
            object token = null;
            // check if there shall be a dataset header 
            if (HasDataSetMessageHeader)
            {
                #region Decode DataSet message header                
                if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0)
                {
                    if (jsonDecoder.ReadField(FieldDataSetWriterId, out token))
                    {
                        DataSetWriterId = Convert.ToUInt16(jsonDecoder.ReadString(FieldDataSetWriterId));
                    }
                }

                if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.SequenceNumber) != 0)
                {
                    if (jsonDecoder.ReadField(FieldSequenceNumber, out token))
                    {
                        SequenceNumber = jsonDecoder.ReadUInt32(FieldSequenceNumber);
                    }
                }

                if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.MetaDataVersion) != 0)
                {
                    if (jsonDecoder.ReadField(FieldMetaDataVersion, out token))
                    {
                        MetaDataVersion = jsonDecoder.ReadEncodeable(FieldMetaDataVersion, typeof(ConfigurationVersionDataType)) as ConfigurationVersionDataType;
                    }
                }

                if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Timestamp) != 0)
                {
                    if (jsonDecoder.ReadField(FieldTimestamp, out token))
                    {
                        Timestamp = jsonDecoder.ReadDateTime(FieldTimestamp);
                    }
                }

                if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Status) != 0)
                {
                    if (jsonDecoder.ReadField(FieldMetaDataVersion, out token))
                    {
                        Status = jsonDecoder.ReadStatusCode(FieldStatus);
                    }
                }
                #endregion

                if (jsonDecoder.ReadField(FieldPayload, out token))
                {
                    TargetVariablesDataType targetVariablesData =
                                ExtensionObject.ToEncodeable(dataSetReader.SubscribedDataSet) as TargetVariablesDataType;
                    DataSetMetaDataType metaDataType = dataSetReader.DataSetMetaData;
                    List<DataValue> dataValues = new List<DataValue>();
                    try
                    {
                        bool wasPush = jsonDecoder.PushStructure(FieldPayload);
                        if (wasPush)
                        {
                            for (int index = 0; index < metaDataType.Fields.Count; index++)
                            {
                                string fieldName = metaDataType.Fields[index].Name;

                                if (jsonDecoder.ReadField(fieldName, out token))
                                {
                                    switch (m_fieldTypeEncoding)
                                    {
                                        case FieldTypeEncodingMask.Variant:
                                            Variant variantValue = jsonDecoder.ReadVariant(fieldName);
                                            dataValues.Add(new DataValue(variantValue));
                                            break;
                                        case FieldTypeEncodingMask.RawData:
                                            if (token is Dictionary<string, object>)
                                            {
                                                return null;
                                            }
                                            dataValues.Add(new DataValue(new Variant(token)));
                                            break;
                                        case FieldTypeEncodingMask.DataValue:                                            
                                            bool wasPush2 = jsonDecoder.PushStructure(fieldName);
                                            try
                                            {
                                                if (wasPush2 && jsonDecoder.ReadField("Value", out token))
                                            {
                                                
                                                    DataValue dataValue = new DataValue(new Variant(token));

                                                    if ((FieldContentMask & DataSetFieldContentMask.StatusCode) != 0)
                                                    {
                                                        dataValue.StatusCode = jsonDecoder.ReadStatusCode("StatusCode");
                                                    }

                                                    if ((FieldContentMask & DataSetFieldContentMask.SourceTimestamp) != 0)
                                                    {
                                                        dataValue.SourceTimestamp = jsonDecoder.ReadDateTime("SourceTimestamp");
                                                    }

                                                    if ((FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) != 0)
                                                    {
                                                        dataValue.SourcePicoseconds = jsonDecoder.ReadUInt16("SourcePicoseconds");
                                                    }

                                                    if ((FieldContentMask & DataSetFieldContentMask.ServerTimestamp) != 0)
                                                    {
                                                        dataValue.ServerTimestamp = jsonDecoder.ReadDateTime("ServerTimestamp");
                                                    }

                                                    if ((FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) != 0)
                                                    {
                                                        dataValue.ServerPicoseconds = jsonDecoder.ReadUInt16("ServerPicoseconds");
                                                    }
                                                    dataValues.Add(dataValue);
                                                }                                                                                              
                                            }
                                            finally
                                            {
                                                jsonDecoder.Pop();
                                            }
                                            break;
                                    }
                                }
                                else
                                {
                                    // the decode failed
                                    return null;
                                }
                            }
                        }
                    }
                    finally
                    {
                        // redo decode stack
                        jsonDecoder.Pop();
                    }
                    List<Field> dataFields = new List<Field>();
                    for (int i = 0; i < dataValues.Count; i++)
                    {
                        Field dataField = new Field();
                        dataField.FieldMetaData = metaDataType.Fields[i];
                        dataField.Value = dataValues[i];
                        // todo investigate if Target attribute and node id are mandatory
                        if (targetVariablesData != null && targetVariablesData.TargetVariables != null
                            && i < targetVariablesData.TargetVariables.Count)
                        {
                            dataField.TargetAttribute = targetVariablesData.TargetVariables[i].AttributeId;
                            dataField.TargetNodeId = targetVariablesData.TargetVariables[i].TargetNodeId;
                        }
                        dataFields.Add(dataField);
                    }
                    DataSet dataSet = new DataSet(metaDataType?.Name);
                    dataSet.Fields = dataFields.ToArray();
                    dataSet.DataSetWriterId = DataSetWriterId;
                    dataSet.SequenceNumber = SequenceNumber;
                    return dataSet;
                }
            }
            return null;
        }
        #endregion

        #region Private Encode Methods
        
        /// <summary>
        /// Encode DataSet message header
        /// </summary>
        /// <param name="encoder"></param>
        private void EncodeDataSetMessageHeader(JsonEncoder encoder)
        {
            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0)
            {                
                encoder.WriteString(FieldDataSetWriterId, DataSetWriterId.ToString());
            }
           
            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.SequenceNumber) != 0)
            {
                encoder.WriteUInt32(FieldSequenceNumber, SequenceNumber);
            }            

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.MetaDataVersion) != 0)
            {
                encoder.WriteEncodeable(FieldMetaDataVersion, MetaDataVersion, typeof(ConfigurationVersionDataType));
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Timestamp) != 0)
            {
                encoder.WriteDateTime(FieldTimestamp, Timestamp);
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Status) != 0)
            {
                encoder.WriteStatusCode(FieldStatus, Status);
            }
        }

        /// <summary>
        /// Encodes The DataSet message payload
        /// </summary>
        /// <param name="jsonEncoder"></param>
        /// <param name="pushStructure"></param>
        internal void EncodePayload(JsonEncoder jsonEncoder, bool pushStructure = true)
        {
            if (pushStructure)
            {
                jsonEncoder.PushStructure(FieldPayload);
            }
            foreach (var field in DataSet.Fields)
            {
                EncodeField(jsonEncoder, field);
            }
            if (pushStructure)
            {
                jsonEncoder.PopStructure();
            }
        }

        /// <summary>
        /// Encodes a dataSet field
        /// </summary>
        /// <param name="encoder"></param>
        /// <param name="field"></param>
        private void EncodeField(JsonEncoder encoder, Field field)
        {
            string fieldName = field.FieldMetaData.Name;
            switch (m_fieldTypeEncoding)
            {
                case FieldTypeEncodingMask.Variant:
                    // If the DataSetFieldContentMask results in a Variant representation,
                    // the field value is encoded as a Variant encoded using the reversible OPC UA JSON Data Encoding
                    // defined in OPC 10000-6.
                    encoder.WriteVariant(fieldName, field.Value.WrappedValue, true);
                    break;
                case FieldTypeEncodingMask.RawData:
                    // If the DataSetFieldContentMask results in a RawData representation,
                    // the field value is a Variant encoded using the non-reversible OPC UA JSON Data Encoding
                    // defined in OPC 10000-6
                    encoder.WriteVariant(fieldName, field.Value.WrappedValue, false);
                    break;
                case FieldTypeEncodingMask.DataValue:
                    DataValue dataValue = new DataValue();

                    dataValue.WrappedValue = field.Value.WrappedValue;

                    if ((FieldContentMask & DataSetFieldContentMask.StatusCode) != 0)
                    {
                        dataValue.StatusCode = field.Value.StatusCode;
                    }

                    if ((FieldContentMask & DataSetFieldContentMask.SourceTimestamp) != 0)
                    {
                        dataValue.SourceTimestamp = field.Value.SourceTimestamp;
                    }

                    if ((FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) != 0)
                    {
                        dataValue.SourcePicoseconds = field.Value.SourcePicoseconds;
                    }

                    if ((FieldContentMask & DataSetFieldContentMask.ServerTimestamp) != 0)
                    {
                        dataValue.ServerTimestamp = field.Value.ServerTimestamp;
                    }

                    if ((FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) != 0)
                    {
                        dataValue.ServerPicoseconds = field.Value.ServerPicoseconds;
                    }
                    // If the DataSetFieldContentMask results in a DataValue representation,
                    // the field value is a DataValue encoded using the non-reversible OPC UA JSON Data Encoding
                    encoder.WriteDataValue(fieldName, dataValue, false);
                    break;
            }
        }        
        #endregion     
    }
}
