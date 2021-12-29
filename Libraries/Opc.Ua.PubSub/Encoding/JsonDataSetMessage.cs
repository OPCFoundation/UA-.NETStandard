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
using System.Linq;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// The JsonDataSetMessage class handler.
    /// It handles the JsonDataSetMessage encoding
    /// </summary>
    public class JsonDataSetMessage : UaDataSetMessage
    {
        #region Fields
        private const string kFieldPayload = "Payload";
        private FieldTypeEncodingMask m_fieldTypeEncoding;
        #endregion

        #region Constructors
        /// <summary>
        /// Create new instance of <see cref="JsonDataSetMessage"/> with DataSet parameter
        /// </summary>
        /// <param name="dataSet"></param>
        public JsonDataSetMessage(DataSet dataSet = null) 
        {
            DataSet = dataSet;
        }
        #endregion

        #region Properties
        /// <summary>
        /// Get JsonDataSetMessageContentMask
        /// The DataSetWriterMessageContentMask defines the flags for the content of the DataSetMessage header.
        /// The Json message mapping specific flags are defined by the <see cref="JsonDataSetMessageContentMask"/> enum.
        /// </summary>
        public JsonDataSetMessageContentMask DataSetMessageContentMask { get; set; }

        /// <summary>
        /// Flag that indicates if the dataset message header is encoded
        /// </summary>
        public bool HasDataSetMessageHeader { get; set; }

        #endregion Properties

        #region Public Methods
        /// <summary>
        /// Set DataSetFieldContentMask
        /// </summary>
        /// <param name="fieldContentMask">The new <see cref="DataSetFieldContentMask"/> for this dataset</param>
        public override void SetFieldContentMask(DataSetFieldContentMask fieldContentMask)
        {
            FieldContentMask = fieldContentMask;

            if (FieldContentMask == DataSetFieldContentMask.None)
            {
                // 00 Variant Field Encoding
                m_fieldTypeEncoding = FieldTypeEncodingMask.Variant;
            }
            else if ((FieldContentMask & DataSetFieldContentMask.RawData) != 0)
            {
                // If the RawData flag is set, all other bits are ignored.
                // 01 RawData Field Encoding
                m_fieldTypeEncoding = FieldTypeEncodingMask.RawData;
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
        }

        /// <summary>
        /// Encodes the dataset message
        /// </summary>
        /// <param name="jsonEncoder">The <see cref="JsonEncoder"/> used to encode this object.</param>
        /// <param name="fieldName">The field name to be used to encode this object, by default it is null.</param>
        public void Encode(JsonEncoder jsonEncoder, string fieldName = null)
        {
            jsonEncoder.PushStructure(fieldName);
            if (HasDataSetMessageHeader)
            {
                EncodeDataSetMessageHeader(jsonEncoder);
            }

            if (DataSet != null)
            {
                EncodePayload(jsonEncoder, HasDataSetMessageHeader);
            }

            jsonEncoder.PopStructure();
        }

        /// <summary>
        /// Decode dataset from the provided json decoder using the provided <see cref="DataSetReaderDataType"/>.
        /// </summary>
        /// <param name="jsonDecoder">The json decoder that contains the json stream.</param>
        /// <param name="messagesCount">Number of Messages found in current jsonDecoder. If 0 then there is SingleDataSetMessage</param>
        /// <param name="messagesListName">The name of the Messages list</param>
        /// <param name="dataSetReader">The <see cref="DataSetReaderDataType"/> used to decode the data set.</param>
        public void DecodePossibleDataSetReader(JsonDecoder jsonDecoder, int messagesCount, string messagesListName, DataSetReaderDataType dataSetReader)
        {
            if (messagesCount == 0)
            {
                // check if there shall be a dataset header and decode it
                if (HasDataSetMessageHeader)
                {
                    DecodeDataSetMessageHeader(jsonDecoder);

                    // push into PayloadStructure if there was a dataset header
                    jsonDecoder.PushStructure(kFieldPayload);
                }

                DecodeErrorReason = ValidateMetadataVersion(dataSetReader?.DataSetMetaData?.ConfigurationVersion);
                if (IsMetadataMajorVersionChange)
                {
                    return;
                }
                // handle single dataset with no network message header & no dataset message header (the content of the payload)
                DataSet = DecodePayloadContent(jsonDecoder, dataSetReader);
            }
            else
            {
                for (int index = 0; index < messagesCount; index++)
                {
                    bool wasPush = jsonDecoder.PushArray(messagesListName, index);
                    if (wasPush)
                    {
                        // atempt decoding the DataSet fields
                        DecodePossibleDataSetReader(jsonDecoder, dataSetReader);

                        // redo jsonDecoder stack
                        jsonDecoder.Pop();

                        if (DataSet != null)
                        {
                            // the dataset was decoded
                            return;
                        }
                       
                    }
                }
            }
        }



        /// <summary>
        /// Atempt to decode dataset from the KeyValue pairs
        /// </summary>
        private void DecodePossibleDataSetReader(JsonDecoder jsonDecoder, DataSetReaderDataType dataSetReader)
        {
            // check if there shall be a dataset header and decode it
            if (HasDataSetMessageHeader)
            {
                DecodeDataSetMessageHeader(jsonDecoder);
            }

            if (dataSetReader.DataSetWriterId != 0 && DataSetWriterId != dataSetReader.DataSetWriterId)
            {
                return;
            }

            object token = null;
            string payloadStructureName = kFieldPayload;
            // try to read "Payload" structure 
            if (!jsonDecoder.ReadField(kFieldPayload, out token))
            {
                // Decode the Messages element in case there is no "Payload" structure
                jsonDecoder.ReadField(null, out token);
                payloadStructureName = null;
            }

            Dictionary<string, object> payload = token as Dictionary<string, object>;

            if (payload != null && dataSetReader.DataSetMetaData != null)
            {
                DecodeErrorReason = ValidateMetadataVersion(dataSetReader.DataSetMetaData.ConfigurationVersion);

                if ( (payload.Count > dataSetReader.DataSetMetaData.Fields.Count) ||
                     IsMetadataMajorVersionChange)
                {
                    // filter out payload that has more fields than the searched datasetMetadata or
                    // doesn't pass metadata version
                    return;
                }
                // check also the field names from reader, if any extra field names then the payload is not matching 
                foreach (string key in payload.Keys)
                {
                    var field = dataSetReader.DataSetMetaData.Fields.FirstOrDefault(f => f.Name == key);
                    if (field == null)
                    {
                        // the field from payload was not found in dataSetReader therefore the payload is not suitable to be decoded
                        return;
                    }
                }
            }
            try
            {
                // try decoding Payload Structure
                bool wasPush = jsonDecoder.PushStructure(payloadStructureName);
                if (wasPush)
                {
                    DataSet = DecodePayloadContent(jsonDecoder, dataSetReader);
                }
            }
            finally
            {
                // redo decode stack
                jsonDecoder.Pop();
            }
        }

        /// <summary>
        /// Decode the Content of the Payload and create a DataSet object from it
        /// </summary>
        private DataSet DecodePayloadContent(JsonDecoder jsonDecoder, DataSetReaderDataType dataSetReader)
        {
            TargetVariablesDataType targetVariablesData =
                ExtensionObject.ToEncodeable(dataSetReader.SubscribedDataSet)
                    as TargetVariablesDataType;

            DataSetMetaDataType dataSetMetaData = dataSetReader.DataSetMetaData;

            object token;
            List<DataValue> dataValues = new List<DataValue>();
            for (int index = 0; index < dataSetMetaData?.Fields.Count; index++)
            {
                FieldMetaData fieldMetaData = dataSetMetaData?.Fields[index];

                if (jsonDecoder.ReadField(fieldMetaData.Name, out token))
                {
                    switch (m_fieldTypeEncoding)
                    {
                        case FieldTypeEncodingMask.Variant:
                            Variant variantValue = jsonDecoder.ReadVariant(fieldMetaData.Name);
                            dataValues.Add(new DataValue(variantValue));
                            break;
                        case FieldTypeEncodingMask.RawData:
                            object value = DecodeRawData(jsonDecoder, dataSetMetaData?.Fields[index], dataSetMetaData?.Fields[index].Name);
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
                                    token = DecodeRawData(jsonDecoder, dataSetMetaData?.Fields[index], "Value");
                                    dataValue = new DataValue(new Variant(token));
                                }
                                else
                                {
                                    // handle Good StatusCode that was not encoded
                                    if (dataSetMetaData?.Fields[index].BuiltInType == (byte)BuiltInType.StatusCode)
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
                            if (dataSetMetaData?.Fields[index].BuiltInType == (byte)BuiltInType.StatusCode)
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

            if (dataValues.Count != dataSetMetaData?.Fields.Count)
            {
                return null;
            }

            //build the DataSet Fields collection based on the decoded values and the target 
            List<Field> dataFields = new List<Field>();
            for (int i = 0; i < dataValues.Count; i++)
            {
                Field dataField = new Field();
                dataField.FieldMetaData = dataSetMetaData?.Fields[i];
                dataField.Value = dataValues[i];

                if (targetVariablesData != null && targetVariablesData.TargetVariables != null
                    && i < targetVariablesData.TargetVariables.Count)
                {
                    // remember the target Attribute and target nodeId
                    dataField.TargetAttribute = targetVariablesData.TargetVariables[i].AttributeId;
                    dataField.TargetNodeId = targetVariablesData.TargetVariables[i].TargetNodeId;
                }
                dataFields.Add(dataField);
            }

            // build the dataset object
            DataSet dataSet = new DataSet(dataSetMetaData?.Name);
            dataSet.DataSetMetaData = dataSetMetaData;
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
        private void EncodeDataSetMessageHeader(IEncoder encoder)
        {
            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0)
            {
                encoder.WriteUInt16(nameof(DataSetWriterId), DataSetWriterId);
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.SequenceNumber) != 0)
            {
                encoder.WriteUInt32(nameof(SequenceNumber), SequenceNumber);
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.MetaDataVersion) != 0)
            {
                encoder.WriteEncodeable(nameof(MetaDataVersion), MetaDataVersion, typeof(ConfigurationVersionDataType));
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Timestamp) != 0)
            {
                encoder.WriteDateTime(nameof(Timestamp), Timestamp);
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Status) != 0)
            {
                encoder.WriteStatusCode(nameof(Status), Status);
            }
        }

        /// <summary>
        /// Encodes The DataSet message payload
        /// </summary>
        internal void EncodePayload(JsonEncoder jsonEncoder, bool pushStructure = true)
        {
            bool forceNamespaceUri = jsonEncoder.ForceNamespaceUri;

            if (pushStructure)
            {
                jsonEncoder.PushStructure(kFieldPayload);
            }

            foreach (var field in DataSet.Fields)
            {
                if (field != null)
                {
                    EncodeField(jsonEncoder, field);
                }
            }

            if (pushStructure)
            {
                jsonEncoder.PopStructure();
            }

            jsonEncoder.ForceNamespaceUri = forceNamespaceUri;
        }

        /// <summary>
        /// Encodes a dataSet field
        /// </summary>
        private void EncodeField(JsonEncoder encoder, Field field)
        {
            string fieldName = field.FieldMetaData.Name;

            Variant valueToEncode = field.Value.WrappedValue;

            // The StatusCode.Good value is not encoded correctly then it shall be committed
            if (valueToEncode == StatusCodes.Good && m_fieldTypeEncoding != FieldTypeEncodingMask.Variant)
            {
                valueToEncode = Variant.Null;
            }

            if (m_fieldTypeEncoding != FieldTypeEncodingMask.DataValue && StatusCode.IsBad(field.Value.StatusCode))
            {
                valueToEncode = field.Value.StatusCode;
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

        #region Private Decode Methods

        /// <summary>
        /// Decode RawData type
        /// </summary>
        /// <returns></returns>
        private object DecodeRawData(JsonDecoder jsonDecoder, FieldMetaData fieldMetaData, string fieldName)
        {
            if (fieldMetaData.BuiltInType != 0)
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
                        Utils.Trace("JsonDataSetMessage - Decoding ValueRank = {0} not supported yet !!!", fieldMetaData.ValueRank);
                    }
                }
                catch (Exception ex)
                {
                    Utils.Trace(ex, "JsonDataSetMessage - Error reading element for RawData.");
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
                if (jsonDecoder.ReadField(nameof(DataSetWriterId), out token))
                {
                    DataSetWriterId = jsonDecoder.ReadUInt16(nameof(DataSetWriterId));
                }
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.SequenceNumber) != 0)
            {
                if (jsonDecoder.ReadField(nameof(SequenceNumber), out token))
                {
                    SequenceNumber = jsonDecoder.ReadUInt32(nameof(SequenceNumber));
                }
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.MetaDataVersion) != 0)
            {
                if (jsonDecoder.ReadField(nameof(MetaDataVersion), out token))
                {
                    MetaDataVersion = jsonDecoder.ReadEncodeable(nameof(MetaDataVersion), typeof(ConfigurationVersionDataType)) as ConfigurationVersionDataType;
                }
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Timestamp) != 0)
            {
                if (jsonDecoder.ReadField(nameof(Timestamp), out token))
                {
                    Timestamp = jsonDecoder.ReadDateTime(nameof(Timestamp));
                }
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Status) != 0)
            {
                if (jsonDecoder.ReadField(nameof(Status), out token))
                {
                    Status = jsonDecoder.ReadStatusCode(nameof(Status));
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
                Utils.Trace(ex, "JsonDataSetMessage - Error decoding field {0}", fieldName);
            }

            return null;
        }
        #endregion
    }
}
