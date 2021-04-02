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
using System.Linq;
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
        /// <param name="fieldContentMask">The new <see cref="DataSetFieldContentMask"/> for this dataset</param>
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
        /// <param name="messageContentMask">The new <see cref="JsonDataSetMessageContentMask"/>.</param>
        public void SetMessageContentMask(JsonDataSetMessageContentMask messageContentMask)
        {
            DataSetMessageContentMask = messageContentMask;
        }

        /// <summary>
        /// Encodes the dataset message
        /// </summary>
        /// <param name="jsonEncoder">The <see cref="JsonEncoder"/> used to encode this object.</param>
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
        /// Decode dataset from the provided json decoder using the provided <see cref="DataSetReaderDataType"/>.
        /// </summary>
        /// <param name="jsonDecoder">The json decoder trhat contains the json stream.</param>
        /// <param name="messagesCount">Number of Messages found in current jsonDecoder.</param>
        /// <param name="messagesListName">The name of the Messages list</param>
        /// <param name="dataSetReader">The <see cref="DataSetReaderDataType"/> used to decode the data set.</param>
        /// <returns>A dataset if decode was successful.</returns>
        public DataSet DecodePossibleDataSetReader(JsonDecoder jsonDecoder, int messagesCount, string messagesListName, DataSetReaderDataType dataSetReader)
        {
            if (messagesCount == 0)
            {
                // check if there shall be a dataset header and decode it
                if (HasDataSetMessageHeader)
                {
                    DecodeDataSetMessageHeader(jsonDecoder);

                    // push into PayloadStructure if there was a dataset header
                    jsonDecoder.PushStructure("Payload");
                }

                // handle single dataset with no network message header & no dataset message header (the content of the payload)
                DataSet dataSet = DecodePayloadContent(jsonDecoder, dataSetReader);

                return dataSet;
            }
            else
            {
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
            }
            return null;
        }

        /// <summary>
        /// Decode dataset from the Keyvalue pairs
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        private DataSet DecodePossibleDataSetReader(JsonDecoder jsonDecoder, DataSetReaderDataType dataSetReader)
        {
            // check if there shall be a dataset header and decode it
            if (HasDataSetMessageHeader)
            {
                DecodeDataSetMessageHeader(jsonDecoder);
            }

            if (dataSetReader.DataSetWriterId != 0 && DataSetWriterId != dataSetReader.DataSetWriterId)
            {
                return null;
            }

            object token = null;
            if (jsonDecoder.ReadField(FieldPayload, out token))
            {
                Dictionary<string, object> payload = token as Dictionary<string, object>;

                if (payload != null)
                {
                    if (payload.Count > dataSetReader.DataSetMetaData.Fields.Count)
                    {
                        // filter out payload that has more fields than the searched datasetMetadata
                        return null;
                    }
                    // check also the field names from reader, if any extra field names then the payload is not matching 
                    foreach (string key in payload.Keys)
                    {
                        var field = dataSetReader.DataSetMetaData.Fields.FirstOrDefault(f => f.Name == key);
                        if (field == null)
                        {
                            // the field from payload was not found in dataSetReader therefore the payload is not suitable to be decoded
                            return null;
                        }
                    }
                }
                try
                {
                    // try decoding Payload Structure
                    bool wasPush = jsonDecoder.PushStructure(FieldPayload);
                    if (wasPush)
                    {
                        return DecodePayloadContent(jsonDecoder, dataSetReader);
                    }
                }
                finally
                {
                    // redo decode stack
                    jsonDecoder.Pop();
                }
            }
            return null;
        }

        /// <summary>
        /// Decode the Content of the Payload and create a DataSet object from it
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        private DataSet DecodePayloadContent(JsonDecoder jsonDecoder, DataSetReaderDataType dataSetReader)
        {
            TargetVariablesDataType targetVariablesData =
                            ExtensionObject.ToEncodeable(dataSetReader.SubscribedDataSet) as TargetVariablesDataType;
            DataSetMetaDataType dataSetMetaData = dataSetReader.DataSetMetaData;

            object token;
            List<DataValue> dataValues = new List<DataValue>();
            for (int index = 0; index < dataSetMetaData.Fields.Count; index++)
            {
                FieldMetaData fieldMetaData = dataSetMetaData.Fields[index];

                if (jsonDecoder.ReadField(fieldMetaData.Name, out token))
                {
                    switch (m_fieldTypeEncoding)
                    {
                        case FieldTypeEncodingMask.Variant:
                            Variant variantValue = jsonDecoder.ReadVariant(fieldMetaData.Name);
                            dataValues.Add(new DataValue(variantValue));
                            break;
                        case FieldTypeEncodingMask.RawData:
                            object value = DecodeRawData(jsonDecoder, dataSetMetaData.Fields[index], dataSetMetaData.Fields[index].Name);
                            dataValues.Add(new DataValue(new Variant(value)));
                            break;
                        case FieldTypeEncodingMask.DataValue:
                            bool wasPush2 = jsonDecoder.PushStructure(fieldMetaData.Name);
                            DataValue dataValue = new DataValue(Variant.Null);
                            try
                            {
                                if (wasPush2 && jsonDecoder.ReadField("Value", out token))
                                {
                                    // the Value was encoded using the non reversible json encoding 
                                    token = DecodeRawData(jsonDecoder, dataSetMetaData.Fields[index], "Value");
                                    dataValue = new DataValue(new Variant(token));
                                }
                                else
                                {
                                    // handle Good StatusCode that was not encoded
                                    if (dataSetMetaData.Fields[index].BuiltInType == (byte)BuiltInType.StatusCode)
                                    {
                                        dataValue = new DataValue(new Variant(new StatusCode(StatusCodes.Good)));
                                    }
                                }

                                if ((FieldContentMask & DataSetFieldContentMask.StatusCode) != 0)
                                {
                                    if (jsonDecoder.ReadField("StatusCode", out token))
                                    {
                                        bool wasPush3 = jsonDecoder.PushStructure("StatusCode");
                                        if (wasPush3)
                                        {
                                            dataValue.StatusCode = jsonDecoder.ReadStatusCode("Code");
                                            jsonDecoder.Pop();
                                        }
                                    }
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
                            finally
                            {
                                if (wasPush2)
                                {
                                    jsonDecoder.Pop();
                                }
                            }
                            break;
                    }
                }
                else
                {
                    switch (m_fieldTypeEncoding)
                    {
                        case FieldTypeEncodingMask.Variant:
                        case FieldTypeEncodingMask.RawData:
                            // handle StatusCodes.Good which is not encoded and therefore must be created at decode
                            if (dataSetMetaData.Fields[index].BuiltInType == (byte)BuiltInType.StatusCode)
                            {
                                dataValues.Add(new DataValue(new Variant(new StatusCode(StatusCodes.Good))));
                            }
                            else
                            {
                                // the field is null
                                dataValues.Add(new DataValue(Variant.Null));
                            }
                            break;
                    }
                }
            }

            if (dataValues.Count != dataSetMetaData.Fields.Count)
            {
                return null;
            }

            //build the DataSet Fields collection based oin the decoded values and the target 
            List<Field> dataFields = new List<Field>();
            for (int i = 0; i < dataValues.Count; i++)
            {
                Field dataField = new Field();
                dataField.FieldMetaData = dataSetMetaData.Fields[i];
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

            // build the dataset object
            DataSet dataSet = new DataSet(dataSetMetaData?.Name);
            dataSet.Fields = dataFields.ToArray();
            dataSet.DataSetWriterId = DataSetWriterId;
            dataSet.SequenceNumber = SequenceNumber;
            return dataSet;
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

            Variant valueToEncode = field.Value.WrappedValue;
            // The StatusCode.Good value is not encoded cor3ectly then it shall be ommited
            if (valueToEncode == StatusCodes.Good && m_fieldTypeEncoding != FieldTypeEncodingMask.Variant)
            {
                valueToEncode = Variant.Null;
            }
            switch (m_fieldTypeEncoding)
            {
                case FieldTypeEncodingMask.Variant:
                    // If the DataSetFieldContentMask results in a Variant representation,
                    // the field value is encoded as a Variant encoded using the reversible OPC UA JSON Data Encoding
                    // defined in OPC 10000-6.
                    encoder.ForceNamespaceUri = false;
                    encoder.WriteVariant(fieldName, valueToEncode, true);
                    break;

                case FieldTypeEncodingMask.RawData:
                    // If the DataSetFieldContentMask results in a RawData representation,
                    // the field value is a Variant encoded using the non-reversible OPC UA JSON Data Encoding
                    // defined in OPC 10000-6
                    encoder.ForceNamespaceUri = true;

                    encoder.WriteVariant(fieldName, valueToEncode, false);
                    break;

                case FieldTypeEncodingMask.DataValue:
                    DataValue dataValue = new DataValue();

                    dataValue.WrappedValue = valueToEncode;

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
                    encoder.ForceNamespaceUri = true;
                    encoder.WriteDataValue(fieldName, dataValue, false);
                    break;
            }
        }
        #endregion

        /// <summary>
        /// Decode RawData type 
        /// </summary>
        /// <returns></returns>
        private object DecodeRawData(JsonDecoder jsonDecoder, FieldMetaData fieldMetaData, string fieldName)
        {
            if (fieldMetaData.BuiltInType != 0)// && fieldMetaData.DataType.Equals(new NodeId(fieldMetaData.BuiltInType)))
            {
                try
                {
                    if (fieldMetaData.ValueRank == ValueRanks.Scalar)
                    {
                        return DecodeRawScalar(jsonDecoder, fieldMetaData.BuiltInType, fieldName);
                    }
                    if (fieldMetaData.ValueRank >= ValueRanks.OneDimension)
                    {

                        return jsonDecoder.ReadArray(fieldName, fieldMetaData.ValueRank, (BuiltInType)fieldMetaData.BuiltInType);
                    }
                    else
                    {
                        Utils.Trace("Decoding ValueRank = {0} not supported yet !!!", fieldMetaData.ValueRank);
                    }
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "Error reading element for RawData.");
                    return (StatusCodes.BadDecodingError);
                }
            }
            return null;
        }

        /// <summary>
        /// Decodes the DataSetMessageHeader
        /// </summary>
        private void DecodeDataSetMessageHeader(JsonDecoder jsonDecoder)
        {
            object token = null;
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
        }

        /// <summary>
        /// Decode a scalar type
        /// </summary>
        private object DecodeRawScalar(JsonDecoder jsonDecoder, byte builtInType, string fieldName)
        {
            try
            {
                switch ((BuiltInType)builtInType)
                {
                    case BuiltInType.Boolean:
                        return jsonDecoder.ReadBoolean(fieldName);
                    case BuiltInType.SByte:
                        return jsonDecoder.ReadSByte(fieldName);
                    case BuiltInType.Byte:
                        return jsonDecoder.ReadByte(fieldName);
                    case BuiltInType.Int16:
                        return jsonDecoder.ReadInt16(fieldName);
                    case BuiltInType.UInt16:
                        return jsonDecoder.ReadUInt16(fieldName);
                    case BuiltInType.Int32:
                        return jsonDecoder.ReadInt32(fieldName);
                    case BuiltInType.UInt32:
                        return jsonDecoder.ReadUInt32(fieldName);
                    case BuiltInType.Int64:
                        return jsonDecoder.ReadInt64(fieldName);
                    case BuiltInType.UInt64:
                        return jsonDecoder.ReadUInt64(fieldName);
                    case BuiltInType.Float:
                        return jsonDecoder.ReadFloat(fieldName);
                    case BuiltInType.Double:
                        return jsonDecoder.ReadDouble(fieldName);
                    case BuiltInType.String:
                        return jsonDecoder.ReadString(fieldName);
                    case BuiltInType.DateTime:
                        return jsonDecoder.ReadDateTime(fieldName);
                    case BuiltInType.Guid:
                        return jsonDecoder.ReadGuid(fieldName);
                    case BuiltInType.ByteString:
                        return jsonDecoder.ReadByteString(fieldName);
                    case BuiltInType.XmlElement:
                        return jsonDecoder.ReadXmlElement(fieldName);
                    case BuiltInType.NodeId:
                        return jsonDecoder.ReadNodeId(fieldName);
                    case BuiltInType.ExpandedNodeId:
                        return jsonDecoder.ReadExpandedNodeId(fieldName);
                    case BuiltInType.QualifiedName:
                        return jsonDecoder.ReadQualifiedName(fieldName);
                    case BuiltInType.LocalizedText:
                        return jsonDecoder.ReadLocalizedText(fieldName);
                    case BuiltInType.DataValue:
                        return jsonDecoder.ReadDataValue(fieldName);
                    case BuiltInType.Enumeration:
                        return jsonDecoder.ReadInt32(fieldName);
                    case BuiltInType.Variant:
                        return jsonDecoder.ReadVariant(fieldName);
                    case BuiltInType.ExtensionObject:
                        return jsonDecoder.ReadExtensionObject(fieldName);
                    case BuiltInType.DiagnosticInfo:
                        return jsonDecoder.ReadDiagnosticInfo(fieldName);
                    case BuiltInType.StatusCode:
                        return jsonDecoder.ReadStatusCode(fieldName);
                }
            }
            catch (Exception ex)
            {
                // log
                Utils.Trace(ex, "JsonDataSetMessage: Error decoding field {0}", fieldName);
            }

            return null;
        }
    }
}
