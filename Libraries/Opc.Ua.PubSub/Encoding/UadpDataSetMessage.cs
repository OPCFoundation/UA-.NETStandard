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

using Opc.Ua.PubSub.PublishedData;
using System;
using System.Collections.Generic;
using System.Xml;
using System.Linq;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// The UADPDataSetMessage class handler.
    /// It handles the UADPDataSetMessage encoding 
    /// </summary>
    public class UadpDataSetMessage : UaDataSetMessage
    {
        #region Fields
        // Validation masks
        private const byte kFieldTypeUsedBits = 0x06;
        private const DataSetFlags1EncodingMask kPreservedDataSetFlags1UsedBits = (DataSetFlags1EncodingMask)0x07;
        private const DataSetFlags1EncodingMask kDataSetFlags1UsedBits = (DataSetFlags1EncodingMask)0xF9;
        #endregion

        #region Constructors

        /// <summary>
        /// Constructor for <see cref="UadpDataSetMessage"/>.
        /// </summary>
        public UadpDataSetMessage() : base()
        {
            // If this bit is set to false, the rest of this DataSetMessage is considered invalid, and shall not be processed by the Subscriber.
            DataSetFlags1 |= DataSetFlags1EncodingMask.MessageIsValid;
        }

        /// <summary>
        /// Constructor for <see cref="UadpDataSetMessage"/> with DataSet parameter
        /// </summary>     
        public UadpDataSetMessage(DataSet dataSet = null) : this()
        {
            DataSet = dataSet;
        }

        #endregion

        #region Properties
        /// <summary>
        /// Get UadpDataSetMessageContentMask
        /// The DataSetWriterMessageContentMask defines the flags for the content of the DataSetMessage header.
        /// The UADP message mapping specific flags are defined by the UadpDataSetMessageContentMask DataType.
        /// </summary>
        public UadpDataSetMessageContentMask DataSetMessageContentMask { get; private set; }

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
        /// Get and Set Pico seconds
        /// </summary>
        public UInt16 PicoSeconds { get; set; }

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
        /// <param name="fieldContentMask">The new <see cref="DataSetFieldContentMask"/> for this dataset</param>
        public override void SetFieldContentMask(DataSetFieldContentMask fieldContentMask)
        {
            FieldContentMask = fieldContentMask;

            #region DataSetFlags1: Bit range 1-2: Field Encoding

            DataSetFlags1 &= kDataSetFlags1UsedBits;

            FieldTypeEncodingMask fieldType = FieldTypeEncodingMask.Reserved;
            if (FieldContentMask == DataSetFieldContentMask.None)
            {
                // 00 Variant Field Encoding
                fieldType = FieldTypeEncodingMask.Variant;
            }
            else if ((FieldContentMask & DataSetFieldContentMask.RawData) != 0)
            {
                // 01 RawData Field Encoding
                fieldType = FieldTypeEncodingMask.RawData;
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

            DataSetFlags1 |= (DataSetFlags1EncodingMask)((byte)fieldType << 1);

            #endregion
        }

        /// <summary>
        /// Set MessageContentMask 
        /// </summary>
        /// <param name="messageContentMask"></param>
        public void SetMessageContentMask(UadpDataSetMessageContentMask messageContentMask)
        {
            DataSetMessageContentMask = messageContentMask;

            DataSetFlags1 &= kPreservedDataSetFlags1UsedBits;
            DataSetFlags2 = 0;

            #region DataSetFlags1: Bit range 3-7: Enabled flags options

            if ((DataSetMessageContentMask & UadpDataSetMessageContentMask.SequenceNumber) != 0)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.SequenceNumber;
            }

            if ((DataSetMessageContentMask & UadpDataSetMessageContentMask.Status) != 0)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.Status;
            }

            if ((DataSetMessageContentMask & UadpDataSetMessageContentMask.MajorVersion) != 0)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion;
            }

            if ((DataSetMessageContentMask & UadpDataSetMessageContentMask.MinorVersion) != 0)
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
            if (DataSet != null && DataSet.IsDeltaFrame)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.DataSetFlags2;
                DataSetFlags2 |= DataSetFlags2EncodingMask.DataDeltaFrame;
            }
            //Always Key frame is sent.
            if ((DataSetMessageContentMask & UadpDataSetMessageContentMask.Timestamp) != 0)
            {
                DataSetFlags1 |= DataSetFlags1EncodingMask.DataSetFlags2;
                DataSetFlags2 |= DataSetFlags2EncodingMask.Timestamp;
            }

            if ((DataSetMessageContentMask & UadpDataSetMessageContentMask.PicoSeconds) != 0)
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
            if ((DataSetFlags2 & DataSetFlags2EncodingMask.DataDeltaFrame) == DataSetFlags2EncodingMask.DataDeltaFrame)
            {
                EncodeMessageDataDeltaFrame(binaryEncoder);
            }
            else
            {
                EncodeMessageDataKeyFrame(binaryEncoder);
            }

            PayloadSizeInStream = (UInt16)(binaryEncoder.Position - StartPositionInStream);

            if (ConfiguredSize > 0 && PayloadSizeInStream < ConfiguredSize)
            {
                PayloadSizeInStream = ConfiguredSize;
                binaryEncoder.Position = StartPositionInStream + PayloadSizeInStream;
            }
        }

        /// <summary>
        /// Atempt to Decode dataset
        /// </summary>
        /// <returns></returns>
        public void DecodePossibleDataSetReader(BinaryDecoder binaryDecoder, DataSetReaderDataType dataSetReader)
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
                        return;
                    }
                }
                else
                {
                    StartPositionInStream = (int)(binaryDecoder.BaseStream.Position);
                }
            }
            if (binaryDecoder.BaseStream.Length <= StartPositionInStream)
            {
                return;
            }
            binaryDecoder.BaseStream.Position = StartPositionInStream;
            DecodeDataSetMessageHeader(binaryDecoder);

            DecodeErrorReason = ValidateMetadataVersion(dataSetReader.DataSetMetaData.ConfigurationVersion);

            if (!IsMetadataMajorVersionChange)
            {
                if ((DataSetFlags2 & DataSetFlags2EncodingMask.DataDeltaFrame) == DataSetFlags2EncodingMask.DataDeltaFrame)
                {
                    DataSet = DecodeMessageDataDeltaFrame(binaryDecoder, dataSetReader);
                }
                else
                {
                    DataSet = DecodeMessageDataKeyFrame(binaryDecoder, dataSetReader);
                }
            }

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
                encoder.WriteDateTime("Timestamp", Timestamp);
            }

            if ((DataSetFlags2 & DataSetFlags2EncodingMask.PicoSeconds) != 0)
            {
                encoder.WriteUInt16("Picoseconds", PicoSeconds);
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.Status) != 0)
            {
                // This is the high order 16 bits of the StatusCode DataType representing
                // the numeric value of the Severity and SubCode of the StatusCode DataType.
                encoder.WriteUInt16("Status", (UInt16)(Status.Code >> 16));
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion) != 0)
            {
                encoder.WriteUInt32("ConfigurationMajorVersion", MetaDataVersion.MajorVersion);
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion) != 0)
            {
                encoder.WriteUInt32("ConfigurationMinorVersion", MetaDataVersion.MinorVersion);
            }
        }

        /// <summary>
        /// Encode payload data
        /// </summary>
        /// <param name="binaryEncoder"></param>
        private void EncodeMessageDataKeyFrame(BinaryEncoder binaryEncoder)
        {
            FieldTypeEncodingMask fieldType = (FieldTypeEncodingMask)(((byte)DataSetFlags1 & kFieldTypeUsedBits) >> 1);
            switch (fieldType)
            {
                case FieldTypeEncodingMask.Variant:
                    binaryEncoder.WriteUInt16("DataSetFieldCount", (UInt16)DataSet.Fields.Length);
                    foreach (Field field in DataSet.Fields)
                    {
                        // 00 Variant type
                        binaryEncoder.WriteVariant("Variant", field.Value.WrappedValue);
                    }
                    break;
                case FieldTypeEncodingMask.DataValue:
                    binaryEncoder.WriteUInt16("DataSetFieldCount", (UInt16)DataSet.Fields.Length);
                    foreach (Field field in DataSet.Fields)
                    {
                        // 10 DataValue type 
                        binaryEncoder.WriteDataValue("DataValue", field.Value);
                    }
                    break;
                case FieldTypeEncodingMask.RawData:
                    // DataSetFieldCount is not persisted for RawData
                    foreach (Field field in DataSet.Fields)
                    {
                        EncodeFieldAsRawData(binaryEncoder, field);
                    }
                    break;
                case FieldTypeEncodingMask.Reserved:
                    // ignore
                    break;
            }
        }

        /// <summary>
        /// Encode payload data delta frame
        /// </summary>
        /// <param name="binaryEncoder"></param>
        private void EncodeMessageDataDeltaFrame(BinaryEncoder binaryEncoder)
        {
            // calculate the number of fields that will be written
            int fieldCount = DataSet.Fields.Count(f => f != null);

            // The field count is written for RadData encoding too unlike for KeyFrame message
            binaryEncoder.WriteUInt16("FieldCount", (UInt16)fieldCount);

            FieldTypeEncodingMask fieldType = (FieldTypeEncodingMask)(((byte)DataSetFlags1 & kFieldTypeUsedBits) >> 1);

            for (int i =0; i <  DataSet.Fields.Length; i++)
            {
                Field field = DataSet.Fields[i];
                if (field == null) continue; // ignore null fields

                // write field index
                binaryEncoder.WriteUInt16("FieldIndex", (UInt16)i);

                switch (fieldType)
                {
                    case FieldTypeEncodingMask.Variant:
                        // 00 Variant type
                        binaryEncoder.WriteVariant("FieldValue", field.Value.WrappedValue);
                        break;
                    case FieldTypeEncodingMask.DataValue:
                        // 10 DataValue type 
                        binaryEncoder.WriteDataValue("FieldValue", field.Value);
                        break;
                    case FieldTypeEncodingMask.RawData:
                        EncodeFieldAsRawData(binaryEncoder, field);
                        break;
                    case FieldTypeEncodingMask.Reserved:
                        // ignore
                        break;
                }
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
                Timestamp = decoder.ReadDateTime("Timestamp");
            }

            if ((DataSetFlags2 & DataSetFlags2EncodingMask.PicoSeconds) != 0)
            {
                PicoSeconds = decoder.ReadUInt16("Picoseconds");
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.Status) != 0)
            {
                // This is the high order 16 bits of the StatusCode DataType representing
                // the numeric value of the Severity and SubCode of the StatusCode DataType.
                UInt16 code = decoder.ReadUInt16("Status");

                Status = ((uint)code) << 16;
            }

            uint minorVersion = kDefaultConfigMinorVersion;
            uint majorVersion = kDefaultConfigMajorVersion;
            if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion) != 0)
            {
                majorVersion = decoder.ReadUInt32("ConfigurationMajorVersion");
            }

            if ((DataSetFlags1 & DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion) != 0)
            {
                minorVersion = decoder.ReadUInt32("ConfigurationMinorVersion");
            }
            MetaDataVersion = new ConfigurationVersionDataType() {
                MinorVersion = minorVersion,
                MajorVersion = majorVersion
            };
        }

        /// <summary>
        ///  Decode field message data key frame from decoder and using a DataSetReader
        /// </summary>
        /// <param name="binaryDecoder"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        private DataSet DecodeMessageDataKeyFrame(BinaryDecoder binaryDecoder, DataSetReaderDataType dataSetReader)
        {
            DataSetMetaDataType dataSetMetaData = dataSetReader.DataSetMetaData;
            try
            {
                ushort fieldCount = 0;
                FieldTypeEncodingMask fieldType = (FieldTypeEncodingMask)(((byte)DataSetFlags1 & kFieldTypeUsedBits) >> 1);
                if (fieldType == FieldTypeEncodingMask.RawData)
                {
                    if (dataSetMetaData != null)
                    {
                        // metadata should provide field count 
                        fieldCount = (ushort)dataSetMetaData.Fields.Count;
                    }
                }
                else
                {
                    fieldCount = binaryDecoder.ReadUInt16("DataSetFieldCount");
                }

                TargetVariablesDataType targetVariablesData =
                   ExtensionObject.ToEncodeable(dataSetReader.SubscribedDataSet) as TargetVariablesDataType;
                
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
                        if (dataSetMetaData != null)
                        {
                            for (int i = 0; i < fieldCount; i++)
                            {
                                FieldMetaData fieldMetaData = dataSetMetaData.Fields[i];
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

                if (dataFields.Count == 0)
                {
                    return null; //the dataset cannot be decoded
                }

                DataSet dataSet = new DataSet(dataSetMetaData?.Name);
                dataSet.DataSetMetaData = dataSetMetaData;
                dataSet.Fields = dataFields.ToArray();
                dataSet.DataSetWriterId = DataSetWriterId;
                dataSet.SequenceNumber = SequenceNumber;
                return dataSet;
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "UadpDataSetMessage.DecodeMessageDataKeyFrame");
                return null;
            }
        }

        /// <summary>
        ///  Decode field message data delta frame from decoder and using a DataSetReader
        /// </summary>
        /// <param name="binaryDecoder"></param>
        /// <param name="dataSetReader"></param>
        /// <returns></returns>
        private DataSet DecodeMessageDataDeltaFrame(BinaryDecoder binaryDecoder, DataSetReaderDataType dataSetReader)
        {
            DataSetMetaDataType dataSetMetaData = dataSetReader.DataSetMetaData;
            try
            {
                FieldTypeEncodingMask fieldType = (FieldTypeEncodingMask)(((byte)DataSetFlags1 & kFieldTypeUsedBits) >> 1);

                if (dataSetMetaData != null)
                {
                    TargetVariablesDataType targetVariablesData =
                        ExtensionObject.ToEncodeable(dataSetReader.SubscribedDataSet) as TargetVariablesDataType;
                   
                    // create dataFields collection
                    List<Field> dataFields = new List<Field>();
                    for (int i = 0; i < dataSetMetaData.Fields.Count; i++)
                    {
                        Field dataField = new Field();
                        dataField.FieldMetaData = dataSetMetaData?.Fields[i];
                        
                        if (targetVariablesData != null && targetVariablesData.TargetVariables != null
                            && i < targetVariablesData.TargetVariables.Count)
                        {
                            // remember the target Attribute and target nodeId
                            dataField.TargetAttribute = targetVariablesData.TargetVariables[i].AttributeId;
                            dataField.TargetNodeId = targetVariablesData.TargetVariables[i].TargetNodeId;
                        }
                        dataFields.Add(dataField);
                    }

                    // read number of fields encoded in this delta frame message
                    ushort fieldCount = fieldCount = binaryDecoder.ReadUInt16("FieldCount");

                    for(int i =0; i < fieldCount; i++)
                    {
                        ushort fieldIndex = binaryDecoder.ReadUInt16("FieldIndex");
                        // update value in dataFields

                        switch (fieldType)
                        {
                            case FieldTypeEncodingMask.Variant:
                                dataFields[fieldIndex].Value = new DataValue(binaryDecoder.ReadVariant("FieldValue"));
                                break;
                            case FieldTypeEncodingMask.DataValue:
                                dataFields[fieldIndex].Value = binaryDecoder.ReadDataValue("FieldValue");
                                break;
                            case FieldTypeEncodingMask.RawData:
                                FieldMetaData fieldMetaData = dataSetMetaData.Fields[fieldIndex];
                                if (fieldMetaData != null)
                                {
                                    var decodedValue = DecodeRawData(binaryDecoder, fieldMetaData);
                                    dataFields[fieldIndex].Value = new DataValue(new Variant(decodedValue));
                                }                                
                                break;
                            case FieldTypeEncodingMask.Reserved:
                                // ignore
                                break;
                        }
                    }

                    DataSet dataSet = new DataSet(dataSetMetaData?.Name);
                    dataSet.DataSetMetaData = dataSetMetaData;
                    dataSet.Fields = dataFields.ToArray();
                    dataSet.IsDeltaFrame = true;
                    dataSet.DataSetWriterId = DataSetWriterId;
                    dataSet.SequenceNumber = SequenceNumber;
                    return dataSet;
                }      
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "UadpDataSetMessage.DecodeMessageDataDeltaFrame");               
            }
            return null;
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
                // 01 RawData Field Encoding 
                var variant = field.Value.WrappedValue;

                if (variant.TypeInfo == null || variant.TypeInfo.BuiltInType == BuiltInType.Null)
                {
                    return;
                }
                object valueToEncode = variant.Value;

                if (field.FieldMetaData.ValueRank == ValueRanks.Scalar)
                {
                    switch ((BuiltInType)field.FieldMetaData.BuiltInType)
                    {
                        case BuiltInType.Boolean:
                            binaryEncoder.WriteBoolean("Bool", Convert.ToBoolean(valueToEncode));
                            break;
                        case BuiltInType.SByte:
                            binaryEncoder.WriteSByte("SByte", Convert.ToSByte(valueToEncode));
                            break;
                        case BuiltInType.Byte:
                            binaryEncoder.WriteByte("Byte", Convert.ToByte(valueToEncode));
                            break;
                        case BuiltInType.Int16:
                            binaryEncoder.WriteInt16("Int16", Convert.ToInt16(valueToEncode));
                            break;
                        case BuiltInType.UInt16:
                            binaryEncoder.WriteUInt16("UInt16", Convert.ToUInt16(valueToEncode));
                            break;
                        case BuiltInType.Int32:
                            binaryEncoder.WriteInt32("Int32", Convert.ToInt32(valueToEncode));
                            break;
                        case BuiltInType.UInt32:
                            binaryEncoder.WriteUInt32("UInt32", Convert.ToUInt32(valueToEncode));
                            break;
                        case BuiltInType.Int64:
                            binaryEncoder.WriteInt64("Int64", Convert.ToInt64(valueToEncode));
                            break;
                        case BuiltInType.UInt64:
                            binaryEncoder.WriteUInt64("UInt64", Convert.ToUInt64(valueToEncode));
                            break;
                        case BuiltInType.Float:
                            binaryEncoder.WriteFloat("Float", Convert.ToSingle(valueToEncode));
                            break;
                        case BuiltInType.Double:
                            binaryEncoder.WriteDouble("Double", Convert.ToDouble(valueToEncode));
                            break;
                        case BuiltInType.DateTime:
                            binaryEncoder.WriteDateTime("DateTime", Convert.ToDateTime(valueToEncode));
                            break;
                        case BuiltInType.Guid:
                            binaryEncoder.WriteGuid("GUID", (Uuid)valueToEncode);
                            break;
                        case BuiltInType.String:
                            binaryEncoder.WriteString("String", valueToEncode as string);
                            break;
                        case BuiltInType.ByteString:
                            binaryEncoder.WriteByteString("ByteString", (byte[])valueToEncode);
                            break;
                        case BuiltInType.QualifiedName:
                            binaryEncoder.WriteQualifiedName("QualifiedName", valueToEncode as QualifiedName);
                            break;
                        case BuiltInType.LocalizedText:
                            binaryEncoder.WriteLocalizedText("LocalizedText", valueToEncode as LocalizedText);
                            break;
                        case BuiltInType.NodeId:
                            binaryEncoder.WriteNodeId("NodeId", valueToEncode as NodeId);
                            break;
                        case BuiltInType.ExpandedNodeId:
                            binaryEncoder.WriteExpandedNodeId("ExpandedNodeId", valueToEncode as ExpandedNodeId);
                            break;
                        case BuiltInType.StatusCode:
                            binaryEncoder.WriteStatusCode("StatusCode", (StatusCode)valueToEncode);
                            break;
                        case BuiltInType.XmlElement:
                            binaryEncoder.WriteXmlElement("XmlElement", valueToEncode as XmlElement);
                            break;
                        case BuiltInType.Enumeration:
                            binaryEncoder.WriteInt32("Enumeration", Convert.ToInt32(valueToEncode));
                            break;
                        case BuiltInType.ExtensionObject:
                            binaryEncoder.WriteExtensionObject("ExtensionObject", valueToEncode as ExtensionObject);
                            break;
                    }
                }
                else if (field.FieldMetaData.ValueRank >= ValueRanks.OneDimension)
                {
                    binaryEncoder.WriteArray(null, valueToEncode, field.FieldMetaData.ValueRank, (BuiltInType)field.FieldMetaData.BuiltInType);
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "Error encoding field {0}.", field.FieldMetaData.Name);
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
                        case ValueRanks.TwoDimensions:
                            return binaryDecoder.ReadArray(null, fieldMetaData.ValueRank, (BuiltInType)fieldMetaData.BuiltInType);

                        case ValueRanks.OneOrMoreDimensions:
                        case ValueRanks.Any:// Scalar or Array with any number of dimensions
                        case ValueRanks.ScalarOrOneDimension:
                        // not implemented

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
