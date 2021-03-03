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
        public UInt16 Status { get; set; }

        /// <summary>
        /// Get DataSet
        /// </summary>
        public DataSet DataSet
        {
            get { return m_dataSet; }
        }

        /// <summary>
        /// Get decoded data DataSets from possible dataset readers
        /// </summary>
        public List<DataSet> DecodedDataSets
        {
            get; private set;
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
            if (HasDataSetMessageHeader)
            {
                EncodeDataSetMessageHeader(jsonEncoder);
            }

            if (DataSet != null)
            {
                EncodePayload(jsonEncoder);
            }
            // jsonEncoder.PopStructure();
            //StartPositionInStream = jsonEncoder.Position;
            //if (DataSetOffset > 0 && StartPositionInStream < DataSetOffset)
            //{
            //    StartPositionInStream = DataSetOffset;
            //    jsonEncoder.Position = DataSetOffset;
            //}           

            //EncodeDataSetMessageHeader(jsonEncoder);
            //EncodePayload(jsonEncoder);

            //PayloadSizeInStream = (UInt16)(jsonEncoder.Position - StartPositionInStream);

            //if (ConfiguredSize > 0 && PayloadSizeInStream < ConfiguredSize)
            //{
            //    PayloadSizeInStream = ConfiguredSize;
            //    jsonEncoder.Position = StartPositionInStream + PayloadSizeInStream;
            //}
        }

        internal void EncodePayload(JsonEncoder jsonEncoder, bool pushStructure = true)
        {
            if (pushStructure)
            {
                jsonEncoder.PushStructure("Payload");
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
        /// Decode dataset
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        public DataSet DecodePossibleDataSetReader(JsonDecoder jsonDecoder, DataSetReaderDataType dataSetReader)
        {
            //UadpDataSetReaderMessageDataType messageSettings = ExtensionObject.ToEncodeable(dataSetReader.MessageSettings)
            //    as UadpDataSetReaderMessageDataType;
            //if (messageSettings != null)
            //{
            //    //StartPositionInStream is calculated but different from reader configuration dataset cannot be decoded
            //    if (StartPositionInStream != messageSettings.DataSetOffset)
            //    {
            //        if (StartPositionInStream == 0)
            //        {
            //            //use configured offset from reader
            //            StartPositionInStream = messageSettings.DataSetOffset;
            //        }
            //        else if (messageSettings.DataSetOffset != 0)
            //        {
            //            //configuration is different from real position in message, the dataset cannot be decoded
            //            return null;
            //        }
            //    }
            //}
            ////if (jsonDecoder.Context.Length <= StartPositionInStream)
            ////{
            ////    return null;
            ////}
            ////jsonDecoder.BaseStream.Position = StartPositionInStream;
            //DecodeDataSetMessageHeader(jsonDecoder);
            //return DecodeFieldMessageData(jsonDecoder, dataSetReader);

            return null;
        }
        #endregion

        #region Encode header & payload

        /// <summary>
        /// Encode DataSet message header
        /// </summary>
        /// <param name="encoder"></param>
        private void EncodeDataSetMessageHeader(JsonEncoder encoder)
        {
            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0)
            {
                encoder.WriteString("DataSetWriterId", DataSetWriterId.ToString());
            }
            else
            {
                encoder.WriteString("DataSetWriterId", null);
            }
            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.SequenceNumber) != 0)
            {
                encoder.WriteUInt32("SequenceNumber", SequenceNumber);
            }
            else
            {
                encoder.WriteUInt32("SequenceNumber", 0);
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.MetaDataVersion) != 0)
            {
                encoder.WriteEncodeable("MetaDataVersion", MetaDataVersion, typeof(ConfigurationVersionDataType));
            }
            else
            {
                encoder.WriteEncodeable("MetaDataVersion", null, typeof(ConfigurationVersionDataType));
            }
            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Timestamp) != 0)
            {
                encoder.WriteDateTime("Timestamp", Timestamp);
            }
            else
            {
                encoder.WriteDateTime("Timestamp", DateTime.MinValue);
            }

            if ((DataSetMessageContentMask & JsonDataSetMessageContentMask.Status) != 0)
            {
                encoder.WriteStatusCode("Status", Status);
            }
            else
            {
                encoder.WriteStatusCode("Status", StatusCodes.Good);
            }



            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.DataSetFlags2) != 0)
            //{
            //    encoder.WriteByte("DataSetFlags2", (byte)DataSetFlags2);
            //}

            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.SequenceNumber) != 0)
            //{
            //    encoder.WriteUInt16("SequenceNumber", (UInt16)SequenceNumber);
            //}

            //if ((DataSetFlags2 & DataSetFlags2EncodingMask.Timestamp) != 0)
            //{
            //    encoder.WriteDateTime("Timestamp", TimeStamp);
            //}

            //if ((DataSetFlags2 & DataSetFlags2EncodingMask.PicoSeconds) != 0)
            //{
            //    encoder.WriteUInt16("Picoseconds", PicoSeconds);
            //}

            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.Status) != 0)
            //{
            //    encoder.WriteUInt16("Status", Status);
            //}

            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion) != 0)
            //{
            //    encoder.WriteUInt32("ConfigurationMajorVersion", ConfigurationMajorVersion);
            //}

            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion) != 0)
            //{
            //    encoder.WriteUInt32("ConfigurationMinorVersion", ConfigurationMinorVersion);
            //}
        }

        /// <summary>
        /// Encodes a dataSet field
        /// </summary>
        /// <param name="encoder"></param>
        /// <param name="field"></param>
        private void EncodeField(JsonEncoder encoder, Field field)
        {
            switch(m_fieldTypeEncoding)
            {
                case FieldTypeEncodingMask.Variant:
                    encoder.WriteVariant(field.FieldMetaData.Name, field.Value.WrappedValue);
                    break;
                case FieldTypeEncodingMask.RawData:
                    EncodeFieldAsRawData(encoder, field);
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
                    encoder.WriteDataValue(field.FieldMetaData.Name, dataValue, false);
                    break;
            }
        }
        #endregion

        #region Decode header & payload

        /// <summary>
        /// Decode DataSet message header
        /// </summary>
        /// <param name="decoder"></param>
        private void DecodeDataSetMessageHeader(IDecoder decoder)
        {
            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.MessageIsValid) != 0)
            //{
            //    DataSetFlags1 = (DataSetFlags1EncodingMask)decoder.ReadByte("DataSetFlags1");
            //}

            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.DataSetFlags2) != 0)
            //{
            //    DataSetFlags2 = (DataSetFlags2EncodingMask)decoder.ReadByte("DataSetFlags2");
            //}

            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.SequenceNumber) != 0)
            //{
            //    SequenceNumber = decoder.ReadUInt16("SequenceNumber");
            //}

            //if ((DataSetFlags2 & DataSetFlags2EncodingMask.Timestamp) != 0)
            //{
            //    TimeStamp = decoder.ReadDateTime("Timestamp");
            //}

            //if ((DataSetFlags2 & DataSetFlags2EncodingMask.PicoSeconds) != 0)
            //{
            //    PicoSeconds = decoder.ReadUInt16("Picoseconds");
            //}

            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.Status) != 0)
            //{
            //    Status = decoder.ReadUInt16("Status");
            //}

            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion) != 0)
            //{
            //    ConfigurationMajorVersion = decoder.ReadUInt32("ConfigurationMajorVersion");
            //}

            //if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion) != 0)
            //{
            //    ConfigurationMinorVersion = decoder.ReadUInt32("ConfigurationMinorVersion");
            //}
        }

        /// <summary>
        ///  Decode field message data from decoder and using a DataSetReader
        /// </summary>
        /// <param name="binaryDecoder"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        private DataSet DecodeFieldMessageData(JsonDecoder binaryDecoder, DataSetReaderDataType dataSetReader)
        {
            //DataSetMetaDataType metaDataType = dataSetReader.DataSetMetaData;
            //try
            //{
            //    ushort fieldCount = 0;
            //    FieldTypeEncodingMask fieldType = (FieldTypeEncodingMask)(((byte)DataSetFlags1 & FieldTypeUsedBits) >> 1);
            //    if (fieldType == FieldTypeEncodingMask.RawData)
            //    {
            //        if (metaDataType != null)
            //        {
            //            // metadata should provide field count 
            //            fieldCount = (ushort)metaDataType.Fields.Count;
            //        }
            //    }
            //    else
            //    {
            //        fieldCount = binaryDecoder.ReadUInt16("DataSetFieldCount");
            //    }

            //    TargetVariablesDataType targetVariablesData =
            //       ExtensionObject.ToEncodeable(dataSetReader.SubscribedDataSet) as TargetVariablesDataType;

            //    if (targetVariablesData == null || targetVariablesData.TargetVariables.Count != fieldCount)
            //    {
            //        // dataset cannot be decoded because the configuration is not for TargetVariables 
            //        return null;
            //    }

            //    // check configuration version
            //    List<DataValue> dataValues = new List<DataValue>();
            //    switch (fieldType)
            //    {
            //        case FieldTypeEncodingMask.Variant:
            //            for (int i = 0; i < fieldCount; i++)
            //            {
            //                dataValues.Add(new DataValue(binaryDecoder.ReadVariant("Variant")));
            //            }
            //            break;
            //        case FieldTypeEncodingMask.DataValue:
            //            for (int i = 0; i < fieldCount; i++)
            //            {
            //                dataValues.Add(binaryDecoder.ReadDataValue("DataValue"));
            //            }
            //            break;
            //        case FieldTypeEncodingMask.RawData:
            //            if (metaDataType != null)
            //            {
            //                for (int i = 0; i < fieldCount; i++)
            //                {
            //                    FieldMetaData fieldMetaData = metaDataType.Fields[i];
            //                    if (fieldMetaData != null)
            //                    {
            //                        var decodedValue = DecodeRawData(binaryDecoder, fieldMetaData);
            //                        dataValues.Add(new DataValue(new Variant(decodedValue)));
            //                    }
            //                }
            //            }
            //            // else the decoding is compromised for RawData type
            //            break;
            //        case FieldTypeEncodingMask.Reserved:
            //            // ignore
            //            break;
            //    }

            //    List<Field> dataFields = new List<Field>();

            //    for (int i = 0; i < dataValues.Count; i++)
            //    {
            //        Field dataField = new Field();
            //        dataField.Value = dataValues[i];
            //        dataField.TargetAttribute = targetVariablesData.TargetVariables[i].AttributeId;
            //        dataField.TargetNodeId = targetVariablesData.TargetVariables[i].TargetNodeId;
            //        dataFields.Add(dataField);
            //    }
            //    DataSet dataSet = new DataSet(metaDataType?.Name);
            //    dataSet.Fields = dataFields.ToArray();
            //    dataSet.DataSetWriterId = DataSetWriterId;
            //    dataSet.SequenceNumber = SequenceNumber;
            //    return dataSet;
            //}
            //catch (Exception ex)
            //{
            //    Utils.Trace(ex, "JsonDataSetMessage.DecodeFieldMessageData");
            //    return null;
            //}
            return null;
        }

        /// <summary>
        /// Encodes field value as RawData
        /// </summary>
        /// <param name="jsonEncoder"></param>
        /// <param name="field"></param>
        private void EncodeFieldAsRawData(JsonEncoder jsonEncoder, Field field)
        {
            string fieldName = field.FieldMetaData.Name;
            try
            {
                // 01 RawData Field Encoding (TODO: StructuredValue)
                var variant = field.Value.WrappedValue;                

                if (variant.TypeInfo == null || variant.TypeInfo.BuiltInType == BuiltInType.Null)
                {
                    return;
                }

                if (field.FieldMetaData.ValueRank == ValueRanks.Scalar)
                {
                    switch ((BuiltInType)field.FieldMetaData.BuiltInType)
                    {
                        case BuiltInType.Boolean:
                            jsonEncoder.WriteBoolean(fieldName, Convert.ToBoolean(variant.Value));
                            break;
                        case BuiltInType.SByte:
                            jsonEncoder.WriteSByte(fieldName, Convert.ToSByte(variant.Value));
                            break;
                        case BuiltInType.Byte:
                            jsonEncoder.WriteByte(fieldName, Convert.ToByte(variant.Value));
                            break;
                        case BuiltInType.Int16:
                            jsonEncoder.WriteInt16(fieldName, Convert.ToInt16(variant.Value));
                            break;
                        case BuiltInType.UInt16:
                            jsonEncoder.WriteUInt16(fieldName, Convert.ToUInt16(variant.Value));
                            break;
                        case BuiltInType.Int32:
                            jsonEncoder.WriteInt32(fieldName, Convert.ToInt32(variant.Value));
                            break;
                        case BuiltInType.UInt32:
                            jsonEncoder.WriteUInt32(fieldName, Convert.ToUInt32(variant.Value));
                            break;
                        case BuiltInType.Int64:
                            jsonEncoder.WriteInt64(fieldName, Convert.ToInt64(variant.Value));
                            break;
                        case BuiltInType.UInt64:
                            jsonEncoder.WriteUInt64(fieldName, Convert.ToUInt64(variant.Value));
                            break;
                        case BuiltInType.Float:
                            jsonEncoder.WriteFloat(fieldName, Convert.ToSingle(variant.Value));
                            break;
                        case BuiltInType.Double:
                            jsonEncoder.WriteDouble(fieldName, Convert.ToDouble(variant.Value));
                            break;
                        case BuiltInType.DateTime:
                            jsonEncoder.WriteDateTime(fieldName, Convert.ToDateTime(variant.Value));
                            break;
                        case BuiltInType.Guid:
                            jsonEncoder.WriteGuid(fieldName, (Uuid)variant.Value);
                            break;
                        case BuiltInType.String:
                            jsonEncoder.WriteString(fieldName, variant.Value as string);
                            break;
                        case BuiltInType.ByteString:
                            jsonEncoder.WriteByteString(fieldName, (byte[])variant.Value);
                            break;
                        case BuiltInType.QualifiedName:
                            jsonEncoder.WriteQualifiedName(fieldName, variant.Value as QualifiedName);
                            break;
                        case BuiltInType.LocalizedText:
                            jsonEncoder.WriteLocalizedText(fieldName, variant.Value as LocalizedText);
                            break;
                        case BuiltInType.NodeId:
                            jsonEncoder.WriteNodeId(fieldName, variant.Value as NodeId);
                            break;
                        case BuiltInType.ExpandedNodeId:
                            jsonEncoder.WriteExpandedNodeId(fieldName, variant.Value as ExpandedNodeId);
                            break;
                        case BuiltInType.StatusCode:
                            jsonEncoder.WriteStatusCode(fieldName, (StatusCode)variant.Value);
                            break;
                        case BuiltInType.XmlElement:
                            jsonEncoder.WriteXmlElement(fieldName, variant.Value as XmlElement);
                            break;
                        case BuiltInType.Enumeration:
                            jsonEncoder.WriteInt32(fieldName, Convert.ToInt32(variant.Value));
                            break;
                        case BuiltInType.ExtensionObject:
                            jsonEncoder.WriteExtensionObject(fieldName, variant.Value as ExtensionObject);
                            break;
                    }
                }
                else
                {
                    switch ((BuiltInType)field.FieldMetaData.BuiltInType)
                    {
                        case BuiltInType.Boolean:
                            jsonEncoder.WriteBooleanArray(fieldName, (bool[])variant.Value);
                            break;
                        case BuiltInType.SByte:
                            jsonEncoder.WriteSByteArray(fieldName, (sbyte[])variant.Value);
                            break;
                        case BuiltInType.Byte:
                            jsonEncoder.WriteByteArray(fieldName, (byte[])variant.Value);
                            break;
                        case BuiltInType.Int16:
                            jsonEncoder.WriteInt16Array(fieldName, (short[])variant.Value);
                            break;
                        case BuiltInType.UInt16:
                            jsonEncoder.WriteUInt16Array(fieldName, (ushort[])variant.Value);
                            break;
                        case BuiltInType.Int32:
                            jsonEncoder.WriteInt32Array(fieldName, (int[])variant.Value);
                            break;
                        case BuiltInType.UInt32:
                            jsonEncoder.WriteUInt32Array(fieldName, (uint[])variant.Value);
                            break;
                        case BuiltInType.Int64:
                            jsonEncoder.WriteInt64Array(fieldName, (long[])variant.Value);
                            break;
                        case BuiltInType.UInt64:
                            jsonEncoder.WriteUInt64Array(fieldName, (ulong[])variant.Value);
                            break;
                        case BuiltInType.Float:
                            jsonEncoder.WriteFloatArray(fieldName, (float[])variant.Value);
                            break;
                        case BuiltInType.Double:
                            jsonEncoder.WriteDoubleArray(fieldName, (double[])variant.Value);
                            break;
                        case BuiltInType.DateTime:
                            jsonEncoder.WriteDateTimeArray(fieldName, (DateTime[])variant.Value);
                            break;
                        case BuiltInType.Guid:
                            jsonEncoder.WriteGuidArray(fieldName, (Uuid[])variant.Value);
                            break;
                        case BuiltInType.String:
                            jsonEncoder.WriteStringArray(fieldName, (string[])variant.Value);
                            break;
                        case BuiltInType.ByteString:
                            jsonEncoder.WriteByteStringArray(fieldName, (byte[][])variant.Value);
                            break;
                        case BuiltInType.QualifiedName:
                            jsonEncoder.WriteQualifiedNameArray(fieldName, (QualifiedName[])variant.Value);
                            break;
                        case BuiltInType.LocalizedText:
                            jsonEncoder.WriteLocalizedTextArray(fieldName, (LocalizedText[])variant.Value);
                            break;
                        case BuiltInType.NodeId:
                            jsonEncoder.WriteNodeIdArray(fieldName, (NodeId[])variant.Value);
                            break;
                        case BuiltInType.ExpandedNodeId:
                            jsonEncoder.WriteExpandedNodeIdArray(fieldName, (ExpandedNodeId[])variant.Value);
                            break;
                        case BuiltInType.StatusCode:
                            jsonEncoder.WriteStatusCodeArray(fieldName, (StatusCode[])variant.Value);
                            break;
                        case BuiltInType.XmlElement:
                            jsonEncoder.WriteXmlElementArray(fieldName, (System.Xml.XmlElement[])variant.Value);
                            break;
                        case BuiltInType.Variant:
                            jsonEncoder.WriteVariantArray(fieldName, (Variant[])variant.Value);
                            break;
                        case BuiltInType.Enumeration:
                            //TODO make this work
                            //binaryEncoder.WriteInt32Array("EnumerationArray", Convert.ToInt32(variant.Value));
                            jsonEncoder.WriteVariantArray(fieldName, (Variant[])variant.Value);
                            break;
                        case BuiltInType.ExtensionObject:
                            jsonEncoder.WriteExtensionObjectArray(fieldName, (ExtensionObject[])variant.Value);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Trace("Error encoding field {0} - {1}", fieldName, ex);
            }
        }

        /// <summary>
        /// Decode RawData type (for SimpleTypeDescription!?)
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="fieldMetaData"></param>
        /// <returns></returns>
        private object DecodeRawData(JsonDecoder jsonDecoder, FieldMetaData fieldMetaData)
        {
            if (fieldMetaData.BuiltInType != 0)// && fieldMetaData.DataType.Equals(new NodeId(fieldMetaData.BuiltInType)))
            {
                try
                {
                    switch (fieldMetaData.ValueRank)
                    {

                        case ValueRanks.Scalar:
                            return DecodeRawScalar(jsonDecoder, fieldMetaData.BuiltInType);

                        case ValueRanks.OneDimension:
                            return DecodeRawArrayOneDimension(jsonDecoder, (BuiltInType)fieldMetaData.BuiltInType);

                        case ValueRanks.TwoDimensions:
                        case ValueRanks.OneOrMoreDimensions:
                        //return DecodeRawArrayMultiDimension(binaryDecoder, (BuiltInType)fieldMetaData.BuiltInType, fieldMetaData.ArrayDimensions);

                        case ValueRanks.Any:// Scalar or Array with any number of dimensions
                        case ValueRanks.ScalarOrOneDimension:
                        //return DecodeRawArrayOrScalar(binaryDecoder, (BuiltInType)fieldMetaData.BuiltInType, fieldMetaData.ArrayDimensions);

                        default:
                            Utils.Trace("Decoding ValueRank = {0} not supported yet !!!", fieldMetaData.ValueRank);
                            break;
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
        /// Decode an array type according to dimensions constraints specified in 6.2.2.1.3 FieldMetaData
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="builtInType"></param>
        /// <returns></returns>
        private object DecodeRawArrayOneDimension(JsonDecoder jsonDecoder, BuiltInType builtInType)
        {

            switch ((BuiltInType)builtInType)
            {
                case BuiltInType.Boolean:
                    return jsonDecoder.ReadBooleanArray(null);
                case BuiltInType.SByte:
                    return jsonDecoder.ReadSByteArray(null);
                case BuiltInType.Byte:
                    return jsonDecoder.ReadByteArray(null);
                case BuiltInType.Int16:
                    return jsonDecoder.ReadInt16Array(null);
                case BuiltInType.UInt16:
                    return jsonDecoder.ReadUInt16Array(null);
                case BuiltInType.Int32:
                    return jsonDecoder.ReadInt32Array(null);
                case BuiltInType.UInt32:
                    return jsonDecoder.ReadUInt32Array(null);
                case BuiltInType.Int64:
                    return jsonDecoder.ReadInt64Array(null);
                case BuiltInType.UInt64:
                    return jsonDecoder.ReadUInt64Array(null);
                case BuiltInType.Float:
                    return jsonDecoder.ReadFloatArray(null);
                case BuiltInType.Double:
                    return jsonDecoder.ReadDoubleArray(null);
                case BuiltInType.String:
                    return jsonDecoder.ReadStringArray(null);
                case BuiltInType.DateTime:
                    return jsonDecoder.ReadDateTimeArray(null);
                case BuiltInType.Guid:
                    return jsonDecoder.ReadGuidArray(null);
                case BuiltInType.ByteString:
                    return jsonDecoder.ReadByteStringArray(null);
                case BuiltInType.XmlElement:
                    return jsonDecoder.ReadXmlElementArray(null);
                case BuiltInType.NodeId:
                    return jsonDecoder.ReadNodeIdArray(null);
                case BuiltInType.ExpandedNodeId:
                    return jsonDecoder.ReadExpandedNodeIdArray(null);
                case BuiltInType.StatusCode:
                    return jsonDecoder.ReadStatusCodeArray(null);
                case BuiltInType.QualifiedName:
                    return jsonDecoder.ReadQualifiedNameArray(null);
                case BuiltInType.LocalizedText:
                    return jsonDecoder.ReadLocalizedTextArray(null);
                case BuiltInType.DataValue:
                    return jsonDecoder.ReadDataValueArray(null);
                case BuiltInType.Enumeration:
                    //return binaryDecoder.ReadInt32Array(null);
                    //return binaryDecoder.ReadEnumeratedArray(null, typeof(Int32));
                    return jsonDecoder.ReadVariantArray(null);
                case BuiltInType.Variant:
                    return jsonDecoder.ReadVariantArray(null);
                case BuiltInType.ExtensionObject:
                    return jsonDecoder.ReadExtensionObjectArray(null);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Decode a scalar type
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="builtInType"></param>
        /// <returns>The decoded object</returns>
        private object DecodeRawScalar(JsonDecoder jsonDecoder, byte builtInType)
        {
            switch ((BuiltInType)builtInType)
            {
                case BuiltInType.Boolean:
                    return jsonDecoder.ReadBoolean(null);
                case BuiltInType.SByte:
                    return jsonDecoder.ReadSByte(null);
                case BuiltInType.Byte:
                    return jsonDecoder.ReadByte(null);
                case BuiltInType.Int16:
                    return jsonDecoder.ReadInt16(null);
                case BuiltInType.UInt16:
                    return jsonDecoder.ReadUInt16(null);
                case BuiltInType.Int32:
                    return jsonDecoder.ReadInt32(null);
                case BuiltInType.UInt32:
                    return jsonDecoder.ReadUInt32(null);
                case BuiltInType.Int64:
                    return jsonDecoder.ReadInt64(null);
                case BuiltInType.UInt64:
                    return jsonDecoder.ReadUInt64(null);
                case BuiltInType.Float:
                    return jsonDecoder.ReadFloat(null);
                case BuiltInType.Double:
                    return jsonDecoder.ReadDouble(null);
                case BuiltInType.String:
                    return jsonDecoder.ReadString(null);
                case BuiltInType.DateTime:
                    return jsonDecoder.ReadDateTime(null);
                case BuiltInType.Guid:
                    return jsonDecoder.ReadGuid(null);
                case BuiltInType.ByteString:
                    return jsonDecoder.ReadByteString(null);
                case BuiltInType.XmlElement:
                    return jsonDecoder.ReadXmlElement(null);
                case BuiltInType.NodeId:
                    return jsonDecoder.ReadNodeId(null);
                case BuiltInType.ExpandedNodeId:
                    return jsonDecoder.ReadExpandedNodeId(null);
                case BuiltInType.StatusCode:
                    return jsonDecoder.ReadStatusCode(null);
                case BuiltInType.QualifiedName:
                    return jsonDecoder.ReadQualifiedName(null);
                case BuiltInType.LocalizedText:
                    return jsonDecoder.ReadLocalizedText(null);
                case BuiltInType.DataValue:
                    return jsonDecoder.ReadDataValue(null);
                case BuiltInType.Enumeration:
                    return jsonDecoder.ReadInt32(null);
                case BuiltInType.Variant:
                    return jsonDecoder.ReadVariant(null);
                case BuiltInType.ExtensionObject:
                    return jsonDecoder.ReadExtensionObject(null);
                default:
                    return null;
            }
        }

        #endregion

    }
}
