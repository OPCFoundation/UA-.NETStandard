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

namespace Opc.Ua.PubSub.Uadp
{
    /// <summary>
    /// The UADPDataSetMessage class handler.
    /// It handles the UADPDataSetMessage encoding 
    /// </summary>
    internal class UadpDataSetMessage
    {
        #region Fields

        // Validation masks
        private const byte FieldTypeUsedBits = 0x06;
        private const DataSetFlags1EncodingMask PreservedDataSetFlags1UsedBits = (DataSetFlags1EncodingMask) 0x07;
        private const DataSetFlags1EncodingMask DataSetFlags1UsedBits = (DataSetFlags1EncodingMask) 0xF9;

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
        public UadpDataSetMessage()
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
        public UadpDataSetMessage(DataSet dataSet = null) : this()
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
        /// Get UadpDataSetMessageContentMask
        /// The DataSetWriterMessageContentMask defines the flags for the content of the DataSetMessage header.
        /// The UADP message mapping specific flags are defined by the UadpDataSetMessageContentMask DataType.
        /// </summary>
        public UadpDataSetMessageContentMask MessageContentMask { get; private set; }

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

            DataSetFlags1 |= (DataSetFlags1EncodingMask)((byte)fieldType<<1) ;

            #endregion
        }

        /// <summary>
        /// Set MessageContentMask 
        /// </summary>
        /// <param name="messageContentMask"></param>
        public void SetMessageContentMask(UadpDataSetMessageContentMask messageContentMask)
        {
            MessageContentMask = messageContentMask;

            DataSetFlags1 &= PreservedDataSetFlags1UsedBits;
            DataSetFlags2 = 0;

            #region DataSetFlags1: Bit range 3-7: Enabled flags options

            if ((MessageContentMask & UadpDataSetMessageContentMask.SequenceNumber) != 0)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.SequenceNumber;
            }
            
            if ((MessageContentMask & UadpDataSetMessageContentMask.Status) != 0)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.Status;
            }

            if ((MessageContentMask & UadpDataSetMessageContentMask.MajorVersion) != 0)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion;
            }

            if ((MessageContentMask & UadpDataSetMessageContentMask.MinorVersion) != 0)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion;
            }

            #endregion

            #region DataSetFlags2

            // Bit range 0-3: UADP DataSetMessage type
            // 0000 Data Key Frame (by default for now)
            // 0001 Data Delta Frame
            // 0010 Event
            // 0011 Keep Alive
            //Always Key frame is sent.
            if ((MessageContentMask & UadpDataSetMessageContentMask.Timestamp) != 0)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.DataSetFlags2;
                DataSetFlags2 |= DataSetFlags2EncodingMask.Timestamp;
            }

            if ((MessageContentMask & UadpDataSetMessageContentMask.PicoSeconds) != 0)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.DataSetFlags2;
                DataSetFlags2 |= DataSetFlags2EncodingMask.PicoSeconds;
            }

            #endregion
        }
        /// <summary>
        /// Encode dataset
        /// </summary>
        /// <param name="binaryEncoder"></param>
        public void Encode(BinaryEncoder binaryEncoder)
        {
            StartPositionInStream = binaryEncoder.Position;
            if (DataSetOffset > 0 && StartPositionInStream < DataSetOffset)
            {
                StartPositionInStream = DataSetOffset;
                binaryEncoder.Position = DataSetOffset;
            }           

            EncodeDataSetMessageHeader(binaryEncoder);
            EncodePayload(binaryEncoder);

            PayloadSizeInStream = (UInt16)(binaryEncoder.Position - StartPositionInStream);

            if (ConfiguredSize > 0 && PayloadSizeInStream < ConfiguredSize)
            {
                PayloadSizeInStream = ConfiguredSize;
                binaryEncoder.Position = StartPositionInStream + PayloadSizeInStream;
            }
        }

        /// <summary>
        /// Decode dataset
        /// </summary>
        /// <param name="binaryDecoder"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        public DataSet DecodePossibleDataSetReader(BinaryDecoder binaryDecoder, DataSetReaderDataType dataSetReader)
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
            if (binaryDecoder.BaseStream.Length <= StartPositionInStream)
            {
                return null;
            }
            binaryDecoder.BaseStream.Position = StartPositionInStream;            
            DecodeDataSetMessageHeader(binaryDecoder);
            return DecodeFieldMessageData(binaryDecoder, dataSetReader); 
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
                encoder.WriteUInt16("SequenceNumber", (UInt16) SequenceNumber);
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
        /// <param name="binaryEncoder"></param>
        private void EncodePayload(BinaryEncoder binaryEncoder)
        {
            FieldTypeEncodingMask fieldType = (FieldTypeEncodingMask)(((byte)DataSetFlags1 & FieldTypeUsedBits) >> 1);
            switch (fieldType)
            {
                case FieldTypeEncodingMask.Variant:
                    binaryEncoder.WriteUInt16("DataSetFieldCount", (UInt16)m_dataSet.Fields.Length);
                    foreach (Field field in m_dataSet.Fields)
                    {
                        // 00 Variant type
                        binaryEncoder.WriteVariant("Variant", field.Value.WrappedValue);
                    }
                    break;
                case FieldTypeEncodingMask.DataValue:
                    binaryEncoder.WriteUInt16("DataSetFieldCount", (UInt16)m_dataSet.Fields.Length);
                    foreach (Field field in m_dataSet.Fields)
                    {
                        // 10 DataValue type 
                        binaryEncoder.WriteDataValue("DataValue", field.Value);
                    }
                    break;
                case FieldTypeEncodingMask.RawData:
                    // DataSetFieldCount is not persisted for RawData
                    foreach (Field field in m_dataSet.Fields)
                    {
                        EncodeFieldAsRawData(binaryEncoder, field);
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
        private DataSet DecodeFieldMessageData(BinaryDecoder binaryDecoder, DataSetReaderDataType dataSetReader)
        {
            DataSetMetaDataType metaDataType = dataSetReader.DataSetMetaData;
            try
            {
                ushort fieldCount = 0;
                FieldTypeEncodingMask fieldType = (FieldTypeEncodingMask)(((byte)DataSetFlags1 & FieldTypeUsedBits) >> 1);
                if (fieldType == FieldTypeEncodingMask.RawData)
                {
                    // metadata should provide field count 
                    fieldCount = (ushort)metaDataType.Fields.Count;
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
                DataSet dataSet = new DataSet(metaDataType.Name);
                dataSet.Fields = dataFields.ToArray();
                dataSet.DataSetWriterId = DataSetWriterId;
                dataSet.SequenceNumber = SequenceNumber;
                return dataSet;
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "UadpDataSetMessage.DecodeFieldMessageData");
                return null;
            }
        }

        /// <summary>
        /// Encodes field value as RawData
        /// </summary>
        /// <param name="binaryEncoder"></param>
        /// <param name="field"></param>
        private void EncodeFieldAsRawData(BinaryEncoder binaryEncoder, Field field)
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
                            binaryEncoder.WriteBoolean("Bool", Convert.ToBoolean(variant.Value));
                            break;
                        case BuiltInType.SByte:
                            binaryEncoder.WriteSByte("SByte", Convert.ToSByte(variant.Value));
                            break;
                        case BuiltInType.Byte:
                            binaryEncoder.WriteByte("Byte", Convert.ToByte(variant.Value));
                            break;
                        case BuiltInType.Int16:
                            binaryEncoder.WriteInt16("Int16", Convert.ToInt16( variant.Value));
                            break;
                        case BuiltInType.UInt16:
                            binaryEncoder.WriteUInt16("UInt16", Convert.ToUInt16(variant.Value));
                            break;
                        case BuiltInType.Int32:
                            binaryEncoder.WriteInt32("Int32", Convert.ToInt32(variant.Value));
                            break;
                        case BuiltInType.UInt32:
                            binaryEncoder.WriteUInt32("UInt32", Convert.ToUInt32(variant.Value));
                            break;
                        case BuiltInType.Int64:
                            binaryEncoder.WriteInt64("Int64", Convert.ToInt64(variant.Value));
                            break;
                        case BuiltInType.UInt64:
                            binaryEncoder.WriteUInt64("UInt64", Convert.ToUInt64(variant.Value));
                            break;
                        case BuiltInType.Float:
                            binaryEncoder.WriteFloat("Float", Convert.ToSingle(variant.Value));
                            break;
                        case BuiltInType.Double:
                            binaryEncoder.WriteDouble("Double", Convert.ToDouble(variant.Value));
                            break;
                        case BuiltInType.DateTime:
                            binaryEncoder.WriteDateTime("DateTime", Convert.ToDateTime(variant.Value));
                            break;
                        case BuiltInType.Guid:
                            binaryEncoder.WriteGuid("GUID", (Uuid)variant.Value);
                            break;
                        case BuiltInType.String:
                            binaryEncoder.WriteString("String", variant.Value as string);
                            break;
                        case BuiltInType.ByteString:
                            binaryEncoder.WriteByteString("ByteString", (byte[])variant.Value);
                            break;
                        case BuiltInType.QualifiedName:
                            binaryEncoder.WriteQualifiedName("QualifiedName", variant.Value as QualifiedName);
                            break;
                        case BuiltInType.LocalizedText:
                            binaryEncoder.WriteLocalizedText("LocalizedText", variant.Value as LocalizedText);
                            break;
                        case BuiltInType.NodeId:
                            binaryEncoder.WriteNodeId("NodeId", variant.Value as NodeId);
                            break;
                        case BuiltInType.ExpandedNodeId:
                            binaryEncoder.WriteExpandedNodeId("ExpandedNodeId", variant.Value as ExpandedNodeId);
                            break;
                        case BuiltInType.StatusCode:
                            binaryEncoder.WriteStatusCode("StatusCode", (StatusCode)variant.Value);
                            break;
                        case BuiltInType.XmlElement:
                            binaryEncoder.WriteXmlElement("XmlElement", variant.Value as XmlElement);
                            break;
                        case BuiltInType.Enumeration:
                            binaryEncoder.WriteInt32("Enumeration", Convert.ToInt32(variant.Value));
                            break;
                        case BuiltInType.ExtensionObject:
                            binaryEncoder.WriteExtensionObject("ExtensionObject", variant.Value as ExtensionObject);
                            break;
                    }
                }
                else
                {
                    switch ((BuiltInType)field.FieldMetaData.BuiltInType)
                    {
                        case BuiltInType.Boolean:
                            binaryEncoder.WriteBooleanArray("BooleanArray", (bool[])variant.Value);
                            break;
                        case BuiltInType.SByte:
                            binaryEncoder.WriteSByteArray("SByteArray", (sbyte[])variant.Value);
                            break;
                        case BuiltInType.Byte:
                            binaryEncoder.WriteByteArray("ByteArray", (byte[])variant.Value);
                            break;
                        case BuiltInType.Int16:
                            binaryEncoder.WriteInt16Array("ByteArray", (short[])variant.Value);
                            break;
                        case BuiltInType.UInt16:
                            binaryEncoder.WriteUInt16Array("UInt16Array", (ushort[])variant.Value);
                            break;
                        case BuiltInType.Int32:
                            binaryEncoder.WriteInt32Array("Int32Array", (int[])variant.Value);
                            break;
                        case BuiltInType.UInt32:
                            binaryEncoder.WriteUInt32Array("UInt32Array", (uint[])variant.Value);
                            break;
                        case BuiltInType.Int64:
                            binaryEncoder.WriteInt64Array("Int64Array", (long[])variant.Value);
                            break;
                        case BuiltInType.UInt64:
                            binaryEncoder.WriteUInt64Array("UInt64Array", (ulong[])variant.Value);
                            break;
                        case BuiltInType.Float:
                            binaryEncoder.WriteFloatArray("FloatArray", (float[])variant.Value);
                            break;
                        case BuiltInType.Double:
                            binaryEncoder.WriteDoubleArray("DoubleArray", (double[])variant.Value);
                            break;
                        case BuiltInType.DateTime:
                            binaryEncoder.WriteDateTimeArray("DateTimeArray", (DateTime[])variant.Value);
                            break;
                        case BuiltInType.Guid:
                            binaryEncoder.WriteGuidArray("GuidArray", (Uuid[])variant.Value);
                            break;
                        case BuiltInType.String:
                            binaryEncoder.WriteStringArray("StringArray", (string[])variant.Value);
                            break;
                        case BuiltInType.ByteString:
                            binaryEncoder.WriteByteStringArray("StringArray", (byte[][])variant.Value);
                            break;
                        case BuiltInType.QualifiedName:
                            binaryEncoder.WriteQualifiedNameArray("QualifiedNameArray", (QualifiedName[])variant.Value);
                            break;
                        case BuiltInType.LocalizedText:
                            binaryEncoder.WriteLocalizedTextArray("LocalizedTextArray", (LocalizedText[])variant.Value);
                            break;
                        case BuiltInType.NodeId:
                            binaryEncoder.WriteNodeIdArray("NodeIdArray", (NodeId[])variant.Value);
                            break;
                        case BuiltInType.ExpandedNodeId:
                            binaryEncoder.WriteExpandedNodeIdArray("ExpandedNodeIdArray", (ExpandedNodeId[])variant.Value);
                            break;
                        case BuiltInType.StatusCode:
                            binaryEncoder.WriteStatusCodeArray("StatusCodeArray", (StatusCode[])variant.Value);
                            break;
                        case BuiltInType.XmlElement:
                            binaryEncoder.WriteXmlElementArray("XmlElementArray", (System.Xml.XmlElement[])variant.Value);
                            break;
                        case BuiltInType.Variant:
                            binaryEncoder.WriteVariantArray("VariantArray", (Variant[])variant.Value);
                            break;
                        case BuiltInType.Enumeration:
                            //TODO make this work
                            //binaryEncoder.WriteInt32Array("EnumerationArray", Convert.ToInt32(variant.Value));
                            binaryEncoder.WriteVariantArray("EnumerationArray", (Variant[])variant.Value);
                            break;
                        case BuiltInType.ExtensionObject:
                            binaryEncoder.WriteExtensionObjectArray("ExtensionObjectArray", (ExtensionObject[])variant.Value);
                            break;
                    }
                }
            }
            catch(Exception ex)
            {
                Utils.Trace("Error encoding field {0} - {1}", field.FieldMetaData.Name, ex);
            }
        }

        /// <summary>
        /// Decode RawData type (for SimpleTypeDescription!?)
        /// </summary>
        /// <param name="binaryDecoder"></param>
        /// <param name="fieldMetaData"></param>
        /// <returns></returns>
        private object DecodeRawData(BinaryDecoder binaryDecoder, FieldMetaData fieldMetaData)
        {
            if (fieldMetaData.BuiltInType != 0)// && fieldMetaData.DataType.Equals(new NodeId(fieldMetaData.BuiltInType)))
            {
                try
                {
                    switch (fieldMetaData.ValueRank)
                    {
                       
                        case ValueRanks.Scalar:
                            return DecodeRawScalar(binaryDecoder, fieldMetaData.BuiltInType);
                       
                        case ValueRanks.OneDimension:
                            return DecodeRawArrayOneDimension(binaryDecoder, (BuiltInType)fieldMetaData.BuiltInType);

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
        /// <param name="binaryDecoder"></param>
        /// <param name="builtInType"></param>
        /// <returns></returns>
        private object DecodeRawArrayOneDimension(BinaryDecoder binaryDecoder, BuiltInType builtInType)
        {

            switch ((BuiltInType)builtInType)
            {
                case BuiltInType.Boolean:
                    return binaryDecoder.ReadBooleanArray(null);
                case BuiltInType.SByte:
                    return binaryDecoder.ReadSByteArray(null);
                case BuiltInType.Byte:
                    return binaryDecoder.ReadByteArray(null);
                case BuiltInType.Int16:
                    return binaryDecoder.ReadInt16Array(null);
                case BuiltInType.UInt16:
                    return binaryDecoder.ReadUInt16Array(null);
                case BuiltInType.Int32:
                    return binaryDecoder.ReadInt32Array(null);
                case BuiltInType.UInt32:
                    return binaryDecoder.ReadUInt32Array(null);
                case BuiltInType.Int64:
                    return binaryDecoder.ReadInt64Array(null);
                case BuiltInType.UInt64:
                    return binaryDecoder.ReadUInt64Array(null);
                case BuiltInType.Float:
                    return binaryDecoder.ReadFloatArray(null);
                case BuiltInType.Double:
                    return binaryDecoder.ReadDoubleArray(null);
                case BuiltInType.String:
                    return binaryDecoder.ReadStringArray(null);
                case BuiltInType.DateTime:
                    return binaryDecoder.ReadDateTimeArray(null);
                case BuiltInType.Guid:
                    return binaryDecoder.ReadGuidArray(null);
                case BuiltInType.ByteString:
                    return binaryDecoder.ReadByteStringArray(null);
                case BuiltInType.XmlElement:
                    return binaryDecoder.ReadXmlElementArray(null);
                case BuiltInType.NodeId:
                    return binaryDecoder.ReadNodeIdArray(null);
                case BuiltInType.ExpandedNodeId:
                    return binaryDecoder.ReadExpandedNodeIdArray(null);
                case BuiltInType.StatusCode:
                    return binaryDecoder.ReadStatusCodeArray(null);
                case BuiltInType.QualifiedName:
                    return binaryDecoder.ReadQualifiedNameArray(null);
                case BuiltInType.LocalizedText:
                    return binaryDecoder.ReadLocalizedTextArray(null);
                case BuiltInType.DataValue:
                    return binaryDecoder.ReadDataValueArray(null);
                case BuiltInType.Enumeration:
                    //return binaryDecoder.ReadInt32Array(null);
                    //return binaryDecoder.ReadEnumeratedArray(null, typeof(Int32));
                    return binaryDecoder.ReadVariantArray(null);
                case BuiltInType.Variant:
                    return binaryDecoder.ReadVariantArray(null);
                case BuiltInType.ExtensionObject:
                    return binaryDecoder.ReadExtensionObjectArray(null);
                    
                default:
                    return null;
            }
        }

        /// <summary>
        /// Decode a scalar type
        /// </summary>
        /// <param name="binaryDecoder"></param>
        /// <param name="builtInType"></param>
        /// <returns>The decoded object</returns>
        private object DecodeRawScalar(BinaryDecoder binaryDecoder, byte builtInType)
        {
            switch ((BuiltInType)builtInType)
            {
                case BuiltInType.Boolean:
                    return binaryDecoder.ReadBoolean(null);
                case BuiltInType.SByte:
                    return binaryDecoder.ReadSByte(null);
                case BuiltInType.Byte:
                    return binaryDecoder.ReadByte(null);
                case BuiltInType.Int16:
                    return binaryDecoder.ReadInt16(null);
                case BuiltInType.UInt16:
                    return binaryDecoder.ReadUInt16(null);
                case BuiltInType.Int32:
                    return binaryDecoder.ReadInt32(null);
                case BuiltInType.UInt32:
                    return binaryDecoder.ReadUInt32(null);
                case BuiltInType.Int64:
                    return binaryDecoder.ReadInt64(null);
                case BuiltInType.UInt64:
                    return binaryDecoder.ReadUInt64(null);
                case BuiltInType.Float:
                    return binaryDecoder.ReadFloat(null);
                case BuiltInType.Double:
                    return binaryDecoder.ReadDouble(null);
                case BuiltInType.String:
                    return binaryDecoder.ReadString(null);
                case BuiltInType.DateTime:
                    return binaryDecoder.ReadDateTime(null);
                case BuiltInType.Guid:
                    return binaryDecoder.ReadGuid(null);
                case BuiltInType.ByteString:
                    return binaryDecoder.ReadByteString(null);
                case BuiltInType.XmlElement:
                    return binaryDecoder.ReadXmlElement(null);
                case BuiltInType.NodeId:
                    return binaryDecoder.ReadNodeId(null);
                case BuiltInType.ExpandedNodeId:
                    return binaryDecoder.ReadExpandedNodeId(null);
                case BuiltInType.StatusCode:
                    return binaryDecoder.ReadStatusCode(null);
                case BuiltInType.QualifiedName:
                    return binaryDecoder.ReadQualifiedName(null);
                case BuiltInType.LocalizedText:
                    return binaryDecoder.ReadLocalizedText(null);
                case BuiltInType.DataValue:
                    return binaryDecoder.ReadDataValue(null);
                case BuiltInType.Enumeration:
                    return binaryDecoder.ReadInt32(null);
                case BuiltInType.Variant:
                    return binaryDecoder.ReadVariant(null);
                case BuiltInType.ExtensionObject:
                    return binaryDecoder.ReadExtensionObject(null);
                default:
                    return null;
            }
        }

        #endregion

    }
}
