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

        // Validation masks
        private const byte FieldTypeUsedBits = 0x06;
        private const DataSetFlags1EncodingMask PreservedDataSetFlags1UsedBits = (DataSetFlags1EncodingMask)0x07;
        private const DataSetFlags1EncodingMask DataSetFlags1UsedBits = (DataSetFlags1EncodingMask)0xF9;

        // UadpDataSetMessage header as byte sizes 
        private const UInt16 DataSetFlags1HeaderSize = 1;
        private const UInt16 DataSetFlags2HeaderSize = 1;
        private const UInt16 SequenceNumberHeaderSize = 2;
        private const UInt16 TimestampHeaderSize = 8;
        private const UInt16 PicosecondsHeaderSize = 2;
        private const UInt16 StatusHeaderSize = 2;
        private const UInt16 ConfigurationMajorVersionHeaderSize = 4;
        private const UInt16 ConfigurationMinorVersionHeaderSize = 4;

        private const UInt16 DataSetFieldCountSize = 2;

        // to avoid unsafe code
        private const UInt16 SizeOfDateTime = 8;
        private const UInt16 SizeOfGuid = 16;

        // Configuration Major and Major current version (VersionTime)
        private const UInt32 ConfigMajorVersion = 1;
        private const UInt32 ConfigMinorVersion = 1;

        private DataSet m_dataSet;

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor
        /// </summary>
        public JsonDataSetMessage()
        {
            ConfigurationMajorVersion = ConfigMajorVersion;
            ConfigurationMinorVersion = ConfigMinorVersion;

            TimeStamp = DateTime.UtcNow;

            // configurable !?
            // If this bit is set to false, the rest of this DataSetMessage is considered invalid, and shall not be processed by the Subscriber.
            DataSetFlags1 |= DataSetFlags1EncodingMask.MessageIsValid;
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
        public JsonDataSetMessageContentMask MessageContentMask { get; private set; }

        #endregion

        #region DataSetMessage settings

        /// <summary>
        /// Get DataSetFlags1
        /// </summary>
        public DataSetFlags1EncodingMask DataSetFlags1 { get; private set; }

        /// <summary>
        /// Get DataSetFlags2
        /// </summary>
        public DataSetFlags2EncodingMask DataSetFlags2 { get; private set; }

        /// <summary>
        /// Get and set the ConfiguredSize of this <see cref="UadpDataSetMessage"/>
        /// </summary>
        public ushort ConfiguredSize { get; set; }

        /// <summary>
        /// Get and set the DataSetOffset of this <see cref="UadpDataSetMessage"/>
        /// </summary>
        public ushort DataSetOffset { get; set; }

        /// <summary>
        /// Get and Set SequenceNumber
        /// A strictly monotonically increasing sequence number assigned by the publisher to each DataSetMessage sent.
        /// </summary>
        public uint SequenceNumber { get; set; }

        /// <summary>
        /// Get and Set Major version
        /// </summary>
        public uint ConfigurationMajorVersion { get; set; }

        /// <summary>
        /// Get and Set Minor version
        /// </summary>
        public uint ConfigurationMinorVersion { get; set; }

        /// <summary>
        /// Get and Set Timestamp
        /// </summary>
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// Get and Set Pico seconds
        /// </summary>
        public UInt16 PicoSeconds { get; set; }

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

        /// <summary>
        /// Get and Set Decoded payload size (hold it here for now)
        /// </summary>
        public UInt16 PayloadSizeInStream { get; set; }

        /// <summary>
        /// Get and Set the startPosition in decoder
        /// </summary>
        public int StartPositionInStream { get; set; }

        #endregion Properties

        #region Public Methods

        /// <summary>
        /// Set DataSetFieldContentMask 
        /// </summary>
        /// <param name="fieldContentMask"></param>
        public void SetFieldContentMask(DataSetFieldContentMask fieldContentMask)
        {
            FieldContentMask = fieldContentMask;

            #region DataSetFlags1: Bit range 1-2: Field Encoding

            DataSetFlags1 &= DataSetFlags1UsedBits;

            FieldTypeEncodingMask fieldType = FieldTypeEncodingMask.Reserved;
            if (FieldContentMask == DataSetFieldContentMask.None)
            {
                // 00 Variant Field Encoding
                fieldType = FieldTypeEncodingMask.Variant;
            }
            else if ((FieldContentMask & (DataSetFieldContentMask.StatusCode
                                          | DataSetFieldContentMask.SourceTimestamp
                                          | DataSetFieldContentMask.ServerTimestamp
                                          | DataSetFieldContentMask.SourcePicoSeconds
                                          | DataSetFieldContentMask.ServerPicoSeconds)) != 0)
            {
                // 10 DataValue Field Encoding
                fieldType = FieldTypeEncodingMask.DataValue;
            }
            else if ((FieldContentMask & DataSetFieldContentMask.RawData) != 0)
            {
                // 01 RawData Field Encoding
                fieldType = FieldTypeEncodingMask.RawData;
            }

            DataSetFlags1 |= (DataSetFlags1EncodingMask)((byte)fieldType << 1);

            #endregion
        }

        /// <summary>
        /// Set MessageContentMask 
        /// </summary>
        /// <param name="messageContentMask"></param>
        public void SetMessageContentMask(JsonDataSetMessageContentMask messageContentMask)
        {
            MessageContentMask = messageContentMask;

            //DataSetFlags1 &= PreservedDataSetFlags1UsedBits;
            //DataSetFlags2 = 0;

            //#region DataSetFlags1: Bit range 3-7: Enabled flags options

            //if ((MessageContentMask & UadpDataSetMessageContentMask.SequenceNumber) != 0)
            //{
            //    DataSetFlags1 |= DataSetFlags1EncodingMask.SequenceNumber;
            //}

            //if ((MessageContentMask & UadpDataSetMessageContentMask.Status) != 0)
            //{
            //    DataSetFlags1 |= DataSetFlags1EncodingMask.Status;
            //}

            //if ((MessageContentMask & UadpDataSetMessageContentMask.MajorVersion) != 0)
            //{
            //    DataSetFlags1 |= DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion;
            //}

            //if ((MessageContentMask & UadpDataSetMessageContentMask.MinorVersion) != 0)
            //{
            //    DataSetFlags1 |= DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion;
            //}

            //#endregion

            //#region DataSetFlags2

            //// Bit range 0-3: UADP DataSetMessage type
            //// 0000 Data Key Frame (by default for now)
            //// 0001 Data Delta Frame
            //// 0010 Event
            //// 0011 Keep Alive
            ////Always Key frame is sent.
            //if ((MessageContentMask & UadpDataSetMessageContentMask.Timestamp) != 0)
            //{
            //    DataSetFlags1 |= DataSetFlags1EncodingMask.DataSetFlags2;
            //    DataSetFlags2 |= DataSetFlags2EncodingMask.Timestamp;
            //}

            //if ((MessageContentMask & UadpDataSetMessageContentMask.PicoSeconds) != 0)
            //{
            //    DataSetFlags1 |= DataSetFlags1EncodingMask.DataSetFlags2;
            //    DataSetFlags2 |= DataSetFlags2EncodingMask.PicoSeconds;
            //}

            //#endregion
        }
        /// <summary>
        /// Encode dataset
        /// </summary>
        /// <param name="jsonEncoder"></param>
        public void Encode(JsonEncoder jsonEncoder)
        {
            //jsonEncoder.PushStructure("Payload");
            EncodePayload(jsonEncoder);
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

        /// <summary>
        /// Decode dataset
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        public DataSet DecodePossibleDataSetReader(JsonDecoder jsonDecoder, DataSetReaderDataType dataSetReader)
        {
            UadpDataSetReaderMessageDataType messageSettings = ExtensionObject.ToEncodeable(dataSetReader.MessageSettings)
                as UadpDataSetReaderMessageDataType;
            if (messageSettings != null)
            {
                //StartPositionInStream is calculated but different from reader configuration dataset cannot be decoded
                if (StartPositionInStream != messageSettings.DataSetOffset)
                {
                    if (StartPositionInStream == 0)
                    {
                        //use configured offset from reader
                        StartPositionInStream = messageSettings.DataSetOffset;
                    }
                    else if (messageSettings.DataSetOffset != 0)
                    {
                        //configuration is different from real position in message, the dataset cannot be decoded
                        return null;
                    }
                }
            }
            //if (jsonDecoder.Context.Length <= StartPositionInStream)
            //{
            //    return null;
            //}
            //jsonDecoder.BaseStream.Position = StartPositionInStream;
            DecodeDataSetMessageHeader(jsonDecoder);
            return DecodeFieldMessageData(jsonDecoder, dataSetReader);
        }
        #endregion

        #region Encode header & payload

        /// <summary>
        /// Encode DataSet message header
        /// </summary>
        /// <param name="encoder"></param>
        private void EncodeDataSetMessageHeader(IEncoder encoder)
        {
            if ((DataSetFlags1 & DataSetFlags1EncodingMask.MessageIsValid) != 0)
            {
                encoder.WriteByte("DataSetFlags1", (byte)DataSetFlags1);
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.DataSetFlags2) != 0)
            {
                encoder.WriteByte("DataSetFlags2", (byte)DataSetFlags2);
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.SequenceNumber) != 0)
            {
                encoder.WriteUInt16("SequenceNumber", (UInt16)SequenceNumber);
            }

            if ((DataSetFlags2 & DataSetFlags2EncodingMask.Timestamp) != 0)
            {
                encoder.WriteDateTime("Timestamp", TimeStamp);
            }

            if ((DataSetFlags2 & DataSetFlags2EncodingMask.PicoSeconds) != 0)
            {
                encoder.WriteUInt16("Picoseconds", PicoSeconds);
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.Status) != 0)
            {
                encoder.WriteUInt16("Status", Status);
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion) != 0)
            {
                encoder.WriteUInt32("ConfigurationMajorVersion", ConfigurationMajorVersion);
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion) != 0)
            {
                encoder.WriteUInt32("ConfigurationMinorVersion", ConfigurationMinorVersion);
            }
        }

        /// <summary>
        /// Encode payload data
        /// </summary>
        /// <param name="jsonEncoder"></param>
        private void EncodePayload(JsonEncoder jsonEncoder)
        {
            FieldTypeEncodingMask fieldType = (FieldTypeEncodingMask)(((byte)DataSetFlags1 & FieldTypeUsedBits) >> 1);
            switch (fieldType)
            {
                case FieldTypeEncodingMask.Variant:
                    jsonEncoder.WriteUInt16("DataSetFieldCount", (UInt16)m_dataSet.Fields.Length);
                    foreach (Field field in m_dataSet.Fields)
                    {
                        // 00 Variant type
                        jsonEncoder.WriteVariant("Variant", field.Value.WrappedValue);
                    }
                    break;
                case FieldTypeEncodingMask.DataValue:
                    jsonEncoder.WriteUInt16("DataSetFieldCount", (UInt16)m_dataSet.Fields.Length);
                    foreach (Field field in m_dataSet.Fields)
                    {
                        // 10 DataValue type 
                        jsonEncoder.WriteDataValue("DataValue", field.Value);
                    }
                    break;
                case FieldTypeEncodingMask.RawData:
                    // DataSetFieldCount is not persisted for RawData
                    foreach (Field field in m_dataSet.Fields)
                    {
                        EncodeFieldAsRawData(jsonEncoder, field);
                    }
                    break;
                case FieldTypeEncodingMask.Reserved:
                    // ignore
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
            if ((DataSetFlags1 & DataSetFlags1EncodingMask.MessageIsValid) != 0)
            {
                DataSetFlags1 = (DataSetFlags1EncodingMask)decoder.ReadByte("DataSetFlags1");
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.DataSetFlags2) != 0)
            {
                DataSetFlags2 = (DataSetFlags2EncodingMask)decoder.ReadByte("DataSetFlags2");
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.SequenceNumber) != 0)
            {
                SequenceNumber = decoder.ReadUInt16("SequenceNumber");
            }

            if ((DataSetFlags2 & DataSetFlags2EncodingMask.Timestamp) != 0)
            {
                TimeStamp = decoder.ReadDateTime("Timestamp");
            }

            if ((DataSetFlags2 & DataSetFlags2EncodingMask.PicoSeconds) != 0)
            {
                PicoSeconds = decoder.ReadUInt16("Picoseconds");
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.Status) != 0)
            {
                Status = decoder.ReadUInt16("Status");
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion) != 0)
            {
                ConfigurationMajorVersion = decoder.ReadUInt32("ConfigurationMajorVersion");
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion) != 0)
            {
                ConfigurationMinorVersion = decoder.ReadUInt32("ConfigurationMinorVersion");
            }
        }

        /// <summary>
        ///  Decode field message data from decoder and using a DataSetReader
        /// </summary>
        /// <param name="binaryDecoder"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        private DataSet DecodeFieldMessageData(JsonDecoder binaryDecoder, DataSetReaderDataType dataSetReader)
        {
            DataSetMetaDataType metaDataType = dataSetReader.DataSetMetaData;
            try
            {
                ushort fieldCount = 0;
                FieldTypeEncodingMask fieldType = (FieldTypeEncodingMask)(((byte)DataSetFlags1 & FieldTypeUsedBits) >> 1);
                if (fieldType == FieldTypeEncodingMask.RawData)
                {
                    if (metaDataType != null)
                    {
                        // metadata should provide field count 
                        fieldCount = (ushort)metaDataType.Fields.Count;
                    }
                }
                else
                {
                    fieldCount = binaryDecoder.ReadUInt16("DataSetFieldCount");
                }

                TargetVariablesDataType targetVariablesData =
                   ExtensionObject.ToEncodeable(dataSetReader.SubscribedDataSet) as TargetVariablesDataType;

                if (targetVariablesData == null || targetVariablesData.TargetVariables.Count != fieldCount)
                {
                    // dataset cannot be decoded because the configuration is not for TargetVariables 
                    return null;
                }

                // check configuration version
                List<DataValue> dataValues = new List<DataValue>();
                switch (fieldType)
                {
                    case FieldTypeEncodingMask.Variant:
                        for (int i = 0; i < fieldCount; i++)
                        {
                            dataValues.Add(new DataValue(binaryDecoder.ReadVariant("Variant")));
                        }
                        break;
                    case FieldTypeEncodingMask.DataValue:
                        for (int i = 0; i < fieldCount; i++)
                        {
                            dataValues.Add(binaryDecoder.ReadDataValue("DataValue"));
                        }
                        break;
                    case FieldTypeEncodingMask.RawData:
                        if (metaDataType != null)
                        {
                            for (int i = 0; i < fieldCount; i++)
                            {
                                FieldMetaData fieldMetaData = metaDataType.Fields[i];
                                if (fieldMetaData != null)
                                {
                                    var decodedValue = DecodeRawData(binaryDecoder, fieldMetaData);
                                    dataValues.Add(new DataValue(new Variant(decodedValue)));
                                }
                            }
                        }
                        // else the decoding is compromised for RawData type
                        break;
                    case FieldTypeEncodingMask.Reserved:
                        // ignore
                        break;
                }

                List<Field> dataFields = new List<Field>();

                for (int i = 0; i < dataValues.Count; i++)
                {
                    Field dataField = new Field();
                    dataField.Value = dataValues[i];
                    dataField.TargetAttribute = targetVariablesData.TargetVariables[i].AttributeId;
                    dataField.TargetNodeId = targetVariablesData.TargetVariables[i].TargetNodeId;
                    dataFields.Add(dataField);
                }
                DataSet dataSet = new DataSet(metaDataType?.Name);
                dataSet.Fields = dataFields.ToArray();
                dataSet.DataSetWriterId = DataSetWriterId;
                dataSet.SequenceNumber = SequenceNumber;
                return dataSet;
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "JsonDataSetMessage.DecodeFieldMessageData");
                return null;
            }
        }

        /// <summary>
        /// Encodes field value as RawData
        /// </summary>
        /// <param name="jsonEncoder"></param>
        /// <param name="field"></param>
        private void EncodeFieldAsRawData(JsonEncoder jsonEncoder, Field field)
        {
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
                            jsonEncoder.WriteBoolean("Bool", Convert.ToBoolean(variant.Value));
                            break;
                        case BuiltInType.SByte:
                            jsonEncoder.WriteSByte("SByte", Convert.ToSByte(variant.Value));
                            break;
                        case BuiltInType.Byte:
                            jsonEncoder.WriteByte("Byte", Convert.ToByte(variant.Value));
                            break;
                        case BuiltInType.Int16:
                            jsonEncoder.WriteInt16("Int16", Convert.ToInt16(variant.Value));
                            break;
                        case BuiltInType.UInt16:
                            jsonEncoder.WriteUInt16("UInt16", Convert.ToUInt16(variant.Value));
                            break;
                        case BuiltInType.Int32:
                            jsonEncoder.WriteInt32("Int32", Convert.ToInt32(variant.Value));
                            break;
                        case BuiltInType.UInt32:
                            jsonEncoder.WriteUInt32("UInt32", Convert.ToUInt32(variant.Value));
                            break;
                        case BuiltInType.Int64:
                            jsonEncoder.WriteInt64("Int64", Convert.ToInt64(variant.Value));
                            break;
                        case BuiltInType.UInt64:
                            jsonEncoder.WriteUInt64("UInt64", Convert.ToUInt64(variant.Value));
                            break;
                        case BuiltInType.Float:
                            jsonEncoder.WriteFloat("Float", Convert.ToSingle(variant.Value));
                            break;
                        case BuiltInType.Double:
                            jsonEncoder.WriteDouble("Double", Convert.ToDouble(variant.Value));
                            break;
                        case BuiltInType.DateTime:
                            jsonEncoder.WriteDateTime("DateTime", Convert.ToDateTime(variant.Value));
                            break;
                        case BuiltInType.Guid:
                            jsonEncoder.WriteGuid("GUID", (Uuid)variant.Value);
                            break;
                        case BuiltInType.String:
                            jsonEncoder.WriteString("String", variant.Value as string);
                            break;
                        case BuiltInType.ByteString:
                            jsonEncoder.WriteByteString("ByteString", (byte[])variant.Value);
                            break;
                        case BuiltInType.QualifiedName:
                            jsonEncoder.WriteQualifiedName("QualifiedName", variant.Value as QualifiedName);
                            break;
                        case BuiltInType.LocalizedText:
                            jsonEncoder.WriteLocalizedText("LocalizedText", variant.Value as LocalizedText);
                            break;
                        case BuiltInType.NodeId:
                            jsonEncoder.WriteNodeId("NodeId", variant.Value as NodeId);
                            break;
                        case BuiltInType.ExpandedNodeId:
                            jsonEncoder.WriteExpandedNodeId("ExpandedNodeId", variant.Value as ExpandedNodeId);
                            break;
                        case BuiltInType.StatusCode:
                            jsonEncoder.WriteStatusCode("StatusCode", (StatusCode)variant.Value);
                            break;
                        case BuiltInType.XmlElement:
                            jsonEncoder.WriteXmlElement("XmlElement", variant.Value as XmlElement);
                            break;
                        case BuiltInType.Enumeration:
                            jsonEncoder.WriteInt32("Enumeration", Convert.ToInt32(variant.Value));
                            break;
                        case BuiltInType.ExtensionObject:
                            jsonEncoder.WriteExtensionObject("ExtensionObject", variant.Value as ExtensionObject);
                            break;
                    }
                }
                else
                {
                    switch ((BuiltInType)field.FieldMetaData.BuiltInType)
                    {
                        case BuiltInType.Boolean:
                            jsonEncoder.WriteBooleanArray("BooleanArray", (bool[])variant.Value);
                            break;
                        case BuiltInType.SByte:
                            jsonEncoder.WriteSByteArray("SByteArray", (sbyte[])variant.Value);
                            break;
                        case BuiltInType.Byte:
                            jsonEncoder.WriteByteArray("ByteArray", (byte[])variant.Value);
                            break;
                        case BuiltInType.Int16:
                            jsonEncoder.WriteInt16Array("ByteArray", (short[])variant.Value);
                            break;
                        case BuiltInType.UInt16:
                            jsonEncoder.WriteUInt16Array("UInt16Array", (ushort[])variant.Value);
                            break;
                        case BuiltInType.Int32:
                            jsonEncoder.WriteInt32Array("Int32Array", (int[])variant.Value);
                            break;
                        case BuiltInType.UInt32:
                            jsonEncoder.WriteUInt32Array("UInt32Array", (uint[])variant.Value);
                            break;
                        case BuiltInType.Int64:
                            jsonEncoder.WriteInt64Array("Int64Array", (long[])variant.Value);
                            break;
                        case BuiltInType.UInt64:
                            jsonEncoder.WriteUInt64Array("UInt64Array", (ulong[])variant.Value);
                            break;
                        case BuiltInType.Float:
                            jsonEncoder.WriteFloatArray("FloatArray", (float[])variant.Value);
                            break;
                        case BuiltInType.Double:
                            jsonEncoder.WriteDoubleArray("DoubleArray", (double[])variant.Value);
                            break;
                        case BuiltInType.DateTime:
                            jsonEncoder.WriteDateTimeArray("DateTimeArray", (DateTime[])variant.Value);
                            break;
                        case BuiltInType.Guid:
                            jsonEncoder.WriteGuidArray("GuidArray", (Uuid[])variant.Value);
                            break;
                        case BuiltInType.String:
                            jsonEncoder.WriteStringArray("StringArray", (string[])variant.Value);
                            break;
                        case BuiltInType.ByteString:
                            jsonEncoder.WriteByteStringArray("StringArray", (byte[][])variant.Value);
                            break;
                        case BuiltInType.QualifiedName:
                            jsonEncoder.WriteQualifiedNameArray("QualifiedNameArray", (QualifiedName[])variant.Value);
                            break;
                        case BuiltInType.LocalizedText:
                            jsonEncoder.WriteLocalizedTextArray("LocalizedTextArray", (LocalizedText[])variant.Value);
                            break;
                        case BuiltInType.NodeId:
                            jsonEncoder.WriteNodeIdArray("NodeIdArray", (NodeId[])variant.Value);
                            break;
                        case BuiltInType.ExpandedNodeId:
                            jsonEncoder.WriteExpandedNodeIdArray("ExpandedNodeIdArray", (ExpandedNodeId[])variant.Value);
                            break;
                        case BuiltInType.StatusCode:
                            jsonEncoder.WriteStatusCodeArray("StatusCodeArray", (StatusCode[])variant.Value);
                            break;
                        case BuiltInType.XmlElement:
                            jsonEncoder.WriteXmlElementArray("XmlElementArray", (System.Xml.XmlElement[])variant.Value);
                            break;
                        case BuiltInType.Variant:
                            jsonEncoder.WriteVariantArray("VariantArray", (Variant[])variant.Value);
                            break;
                        case BuiltInType.Enumeration:
                            //TODO make this work
                            //binaryEncoder.WriteInt32Array("EnumerationArray", Convert.ToInt32(variant.Value));
                            jsonEncoder.WriteVariantArray("EnumerationArray", (Variant[])variant.Value);
                            break;
                        case BuiltInType.ExtensionObject:
                            jsonEncoder.WriteExtensionObjectArray("ExtensionObjectArray", (ExtensionObject[])variant.Value);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Trace("Error encoding field {0} - {1}", field.FieldMetaData.Name, ex);
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
